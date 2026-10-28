using System.Numerics;
using Silk.NET.Input;

namespace Moose.Graphics.Input;

public interface IInputHandler
{
    void KeyDown(Key key, KeyModifiers modifiers) { }

    void TextInput(string text) { }

    void MouseDown(MouseButton button, Vector2 position, KeyModifiers modifiers) { }

    void MouseUp(MouseButton button, Vector2 position, KeyModifiers modifiers) { }

    void MouseMove(Vector2 position) { }

    void Scroll(Vector2 position, float delta, KeyModifiers modifiers) { }

    void FocusChanged(bool focused) { }
}
