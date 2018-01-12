﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quaver.Logging;

using Quaver.Utility;
using Quaver.Graphics.Text;

namespace Quaver.Graphics.Button
{
    /// <summary>
    /// This class will be inherited from every button class.
    /// </summary>
    internal class KeyBindButton : Button
    {
        internal TextBoxSprite TextSprite { get; set; }

        internal KeyBindButton(Vector2 ButtonSize, string ButtonText)
        {
            TextSprite = new TextBoxSprite()
            {
                Text = ButtonText,
                Size = new UDim2(ButtonSize.X, ButtonSize.Y),
                Alignment = Alignment.MidCenter,
                TextAlignment = Alignment.MidCenter,
                Parent = this
            };
            Size.X.Offset = ButtonSize.X;
            Size.Y.Offset = ButtonSize.Y;
            Image = GameBase.UI.BlankBox;
            TextSprite.TextColor = Color.Black;
        }

        /// <summary>
        ///     Current tween value of the object. Used for animation.
        /// </summary>
        private float HoverCurrentTween { get; set; }

        /// <summary>
        ///     Target tween value of the object. Used for animation.
        /// </summary>
        private float HoverTargetTween { get; set; }

        /// <summary>
        ///     Current Color/Tint of the object.
        /// </summary>
        private Color CurrentTint = Color.White;

        /// <summary>
        ///     This method is called when the mouse hovers over the button
        /// </summary>
        internal override void MouseOver()
        {
            HoverTargetTween = 1;
        }

        /// <summary>
        ///     This method is called when the Mouse hovers out of the button
        /// </summary>
        internal override void MouseOut()
        {
            HoverTargetTween = 0;
        }

        /// <summary>
        ///     This method will be used for button logic and animation
        /// </summary>
        internal override void Update(double dt)
        {
            HoverCurrentTween = Util.Tween(HoverTargetTween, HoverCurrentTween, Math.Min(dt / 40, 1));
            CurrentTint.R = (byte)(((HoverCurrentTween * 0.25) + 0.75f) * 255);
            CurrentTint.G = (byte)(((HoverCurrentTween * 0.5) + 0.5f) * 255);
            CurrentTint.B = (byte)(((HoverCurrentTween * 0.25) + 0.75f) * 255);
            Tint = CurrentTint;

            //TextSprite.Update(dt);
            base.Update(dt);
        }
    }
}