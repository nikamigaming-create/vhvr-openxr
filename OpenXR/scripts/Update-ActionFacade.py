"""Generate neutral action paths from the tracked upstream action catalog."""
from pathlib import Path
import re

repo = Path(__file__).resolve().parents[2]
catalog = repo / 'Unity/ValheimVR/Assets/SteamVR_Input/SteamVR_Input_Actions.cs'
text = catalog.read_text(encoding='utf-8-sig')
set_text = (catalog.parent / 'SteamVR_Input_ActionSets.cs').read_text(encoding='utf-8-sig')
sets = re.findall(r'SteamVR_Actions\.p_(\w+)\s*=.*?SteamVR_ActionSet\.Create<[^>]+>\("([^"]+)"\)', set_text)
types = {'Boolean': 'VRBooleanAction', 'Vector2': 'VRVector2Action', 'Pose': 'VRPoseAction', 'Vibration': 'VRHapticAction'}
actions = re.findall(r'SteamVR_Actions\.p_(\w+)\s*=.*?SteamVR_Action\.Create<SteamVR_Action_(\w+)>\("([^"]+)"\)', text)
lines = ['// Generated from the tracked upstream action catalog by Update-ActionFacade.py.',
         'namespace ValheimVRMod.VRCore.Backends', '{', '    public static class VRInputActions', '    {']
assert sets, 'Upstream action-set catalog format changed'
for name, path in sets:
    name = 'Default' if name == '_default' else name
    lines.append(f'        public static VRActionSet {name} {{ get; }} = new VRActionSet("{path}");')
for name, kind, path in actions:
    if kind in types:
        action_type = types[kind]
        lines.append(f'        public static {action_type} {name} {{ get; }} = new {action_type}("{path}");')
lines += ['    }', '}', '']
assert len(actions) > 40, 'Upstream action catalog format changed'
(repo / 'ValheimVRMod/VRCore/Backends/VRInputActions.cs').write_text('\n'.join(lines), encoding='utf-8')
print(f'Generated {len(sets)} neutral sets and {sum(kind in types for _, kind, _ in actions)} action entries')
