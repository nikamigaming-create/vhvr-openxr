using UnityEngine;
using UnityEngine.EventSystems;

namespace Nikami.OpenXR;

internal static class OpenXRInputFocus
{
    static bool lostDesktopFocus;

    internal static void Install()
    {
        Application.focusChanged += FocusChanged;
        Application.quitting += Shutdown;
    }

    static void FocusChanged(bool focused)
    {
        if (!OpenXRPlugin.Ready) return;
        if (!focused)
        {
            lostDesktopFocus = true;
            return;
        }
        if (!lostDesktopFocus) return;
        lostDesktopFocus = false;

        // Unity restores the keyboard on Alt-Tab, but Valheim's chat input
        // retains its TMP focus and PlayerController.TakeInput keeps returning
        // false. Use the native chat-cancel path instead of resetting devices,
        // unpausing the world, or overriding the player's open menus.
        var chat = Chat.instance;
        if (!chat || !chat.HasFocus() || !chat.m_input) return;
        var events = EventSystem.current;
        var selected = events ? events.currentSelectedGameObject : null;
        if (selected && selected.transform.IsChildOf(chat.m_input.transform))
            events.SetSelectedGameObject(null);
        chat.m_input.gameObject.SetActive(false);
        chat.m_wasFocused = false;
        // Do not call SendInput/InputText: regaining focus must never submit
        // a draft or execute text that happens to be a chat command.
        OpenXRPlugin.Log.LogInfo("OpenXR input focus restored: released chat capture; unsent text retained.");
    }

    static void Shutdown()
    {
        Application.focusChanged -= FocusChanged;
        Application.quitting -= Shutdown;
        lostDesktopFocus = false;
    }
}
