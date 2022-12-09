namespace ThriveDevCenter.Shared.Models.Enums;

using System;

public static class TerminalColourExtensions
{
    public static string ColourToCSSHexValue(this TerminalColour colour)
    {
        switch (colour)
        {
            case TerminalColour.White:
                return "#FFFFFF";
            case TerminalColour.Black:
                return "#5E5C64";
            case TerminalColour.Red:
                return "#F66151";
            case TerminalColour.Green:
                return "#33DA7A";
            case TerminalColour.Yellow:
                return "#E9AD0C";
            case TerminalColour.Blue:
                return "#2A7BDE";
            case TerminalColour.Magenta:
                return "#C061CB";
            case TerminalColour.Cyan:
                return "#33C7DE";
            case TerminalColour.DarkWhite:
                return "#D0CFCC";
            case TerminalColour.DarkBlack:
                return "#171421";
            case TerminalColour.DarkRed:
                return "#C01C28";
            case TerminalColour.DarkGreen:
                return "#26A269";
            case TerminalColour.DarkYellow:
                return "#A2734C";
            case TerminalColour.DarkBlue:
                return "#12488B";
            case TerminalColour.DarkMagenta:
                return "#A347BA";
            case TerminalColour.DarkCyan:
                return "#2AA1B3";
            default:
                throw new ArgumentOutOfRangeException(nameof(colour), colour, null);
        }
    }
}
