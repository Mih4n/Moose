namespace Moose.Core.Settings;

public static partial class Keys
{
    public static class Colors
    {
        public const string Section = "colors";
        public const string Foreground = "colors:foreground";
        public const string Background = "colors:background";
        public const string Cursor = "colors:cursor";
        public const string CursorText = "colors:cursor_text";
        public const string SelectionForeground = "colors:selection_foreground";
        public const string SelectionBackground = "colors:selection_background";

        public static class Normal
        {
            public const string Section = "colors:normal";
            public const string Black = "colors:normal:black";
            public const string Red = "colors:normal:red";
            public const string Green = "colors:normal:green";
            public const string Yellow = "colors:normal:yellow";
            public const string Blue = "colors:normal:blue";
            public const string Magenta = "colors:normal:magenta";
            public const string Cyan = "colors:normal:cyan";
            public const string White = "colors:normal:white";

            public static readonly string[] All = [Black, Red, Green, Yellow, Blue, Magenta, Cyan, White];
        }

        public static class Bright
        {
            public const string Section = "colors:bright";
            public const string Black = "colors:bright:black";
            public const string Red = "colors:bright:red";
            public const string Green = "colors:bright:green";
            public const string Yellow = "colors:bright:yellow";
            public const string Blue = "colors:bright:blue";
            public const string Magenta = "colors:bright:magenta";
            public const string Cyan = "colors:bright:cyan";
            public const string White = "colors:bright:white";

            public static readonly string[] All = [Black, Red, Green, Yellow, Blue, Magenta, Cyan, White];
        }
    }
}
