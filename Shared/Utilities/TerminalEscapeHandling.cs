namespace ThriveDevCenter.Shared.Converters
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Models.Enums;

    public class TerminalEscapeHandling
    {
        private const string TerminalEscape = "\x1B";
        private const TerminalColour DefaultColour = TerminalColour.White;

        public static IEnumerable<TextSection> HandleTerminalEscapes(string rawText)
        {
            int sectionStart = -1;
            int sectionEnd = -1;

            int matchPosition = 0;
            bool inCommand = false;

            var sectionColour = DefaultColour;

            for (int i = 0; i < rawText.Length; ++i)
            {
                if (inCommand)
                {
                    if (rawText[i] == 'm')
                    {
                        // Command ended
                        if (sectionStart != -1)
                        {
                            // Parse the commands

                            if (rawText[sectionStart] == '[')
                            {
                                // A colour command
                                ++sectionStart;
                                sectionColour = HandleColourCommand(rawText.Substring(sectionStart, i - sectionStart),
                                    sectionColour);
                            }
                        }

                        inCommand = false;
                        sectionStart = -1;
                        continue;
                    }

                    if (sectionStart == -1)
                        sectionStart = i;

                    continue;
                }

                if (rawText[i] == TerminalEscape[matchPosition])
                {
                    ++matchPosition;

                    if (matchPosition >= TerminalEscape.Length)
                    {
                        if (sectionStart != -1)
                        {
                            // End the previous section
                            yield return new TextSection()
                            {
                                Colour = sectionColour,
                                Text = rawText.Substring(sectionStart, sectionEnd - sectionStart + 1)
                            };

                            sectionStart = -1;
                            sectionEnd = -1;
                        }

                        // Found start of terminal command
                        inCommand = true;
                        matchPosition = 0;
                    }

                    continue;
                }
                else
                {
                    matchPosition = 0;
                }

                if (sectionStart == -1)
                    sectionStart = i;

                sectionEnd = i;
            }

            // Final section to the end of the string
            if (sectionStart != -1)
            {
                yield return new TextSection()
                {
                    Colour = sectionColour,
                    Text = rawText.Substring(sectionStart)
                };
            }
        }

        private static TerminalColour HandleColourCommand(string commands, TerminalColour sectionColour)
        {
            foreach (var command in commands.Split(';'))
            {
                var code = Convert.ToInt32(command);

                switch (code)
                {
                    case 0:
                        sectionColour = DefaultColour;
                        break;
                    case 30:
                        sectionColour = TerminalColour.DarkBlack;
                        break;
                    case 31:
                        sectionColour = TerminalColour.DarkRed;
                        break;
                    case 32:
                        sectionColour = TerminalColour.DarkGreen;
                        break;
                    case 33:
                        sectionColour = TerminalColour.DarkYellow;
                        break;
                    case 34:
                        sectionColour = TerminalColour.DarkBlue;
                        break;
                    case 35:
                        sectionColour = TerminalColour.DarkMagenta;
                        break;
                    case 36:
                        sectionColour = TerminalColour.DarkCyan;
                        break;
                    case 37:
                        sectionColour = TerminalColour.DarkWhite;
                        break;
                    case 39:
                        sectionColour = DefaultColour;
                        break;
                    case 90:
                        sectionColour = TerminalColour.Black;
                        break;
                    case 91:
                        sectionColour = TerminalColour.Red;
                        break;
                    case 92:
                        sectionColour = TerminalColour.Green;
                        break;
                    case 93:
                        sectionColour = TerminalColour.Yellow;
                        break;
                    case 94:
                        sectionColour = TerminalColour.Blue;
                        break;
                    case 95:
                        sectionColour = TerminalColour.Magenta;
                        break;
                    case 96:
                        sectionColour = TerminalColour.Cyan;
                        break;
                    case 97:
                        sectionColour = TerminalColour.White;
                        break;
                }
            }

            return sectionColour;
        }

        public struct TextSection
        {
            public TerminalColour Colour;
            public string Text;
        }
    }
}
