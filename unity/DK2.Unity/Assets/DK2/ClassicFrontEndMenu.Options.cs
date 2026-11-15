// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class ClassicFrontEndMenu
    {
        /// <summary>The key a settings page is waiting to bind: the keyboard is Unity's, the binding is the model's.</summary>
        private void PollKeyCapture()
        {
            OptionsPagesController options = Model.Options;
            if (options == null || Model.Page != options.Page || options.CapturingAction == null) return;
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
                if ((int)key < (int)KeyCode.Mouse0 && Input.GetKeyDown(key))
                    options.CaptureKey(ClassicKeyBindings.ScanCode(key), ClassicKeyBindings.Modifiers);
        }
    }
}
