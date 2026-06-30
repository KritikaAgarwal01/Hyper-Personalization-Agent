using CCH.HPSO.Azure.Shared.DataModel;
using System.Text.RegularExpressions;

namespace CCH.HPSO.Azure.Shared.Helpers
{
    /// <summary>
    /// Th PlaceholderHelper class provides methods to extract and replace placeholders in a given text.
    /// </summary>
    public static class PlaceholderHelper
    {
        /// <summary>
        /// The ExtractPlaceholders method to extract placeholders from the prompt text
        /// </summary>
        /// <param name="promptText">The text containing placeholders.</param>
        /// <returns>Returns a list of parsed placeholder information.</returns>
        public static List<PlaceHolderInformation> ExtractPlaceholders(string promptText)
        {
            var placeholders = new List<PlaceHolderInformation>();

            if (string.IsNullOrEmpty(promptText))
            {
                return placeholders;
            }

            var matches = Regex.Matches(promptText, @"\{([a-zA-Z0-9_]+)\}", RegexOptions.None, TimeSpan.FromMilliseconds(100));

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    string placeholderValue = match.Groups[0].Value;
                    if (!placeholders.Any(p => p.Placeholder == placeholderValue))
                    {
                        placeholders.Add(new PlaceHolderInformation
                        {
                            Placeholder = placeholderValue,
                            TraversalPath = string.Empty,
                            ActualValue = string.Empty
                        });
                    }
                }
            }

            return placeholders;
        }

        /// <summary>
        /// The ReplacePlaceholders method to update the prompt text placeholders with their actual values
        /// </summary>
        /// <param name="promptText">The prompt text</param>
        /// <param name="placeholders">The placeholder having actual values</param>
        /// <param name="inputMessage">The input message object</param>
        /// <returns>Returns the updated prompt text with placeholders replaced by their actual values.</returns>
        public static string ReplacePlaceholders(string promptText, List<PlaceHolderInformation> placeholders, InputMessage inputMessage)
        {
            if (string.IsNullOrEmpty(promptText) || placeholders == null || placeholders.Count == 0)
            {
                return promptText;
            }

            foreach (var placeholder in placeholders.Where(p => !string.IsNullOrEmpty(p.ActualValue) && !string.IsNullOrEmpty(p.Placeholder)))
            {
                promptText = promptText.Replace(placeholder.Placeholder!, placeholder.ActualValue);
            }

            return promptText;
        }
    }
}
