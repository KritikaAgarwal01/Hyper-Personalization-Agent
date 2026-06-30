using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCH.HPSO.Azure.Shared.DataModel
{

    /// <summary>
    /// Represents the response from the API containing OpenAI text output and related metadata.
    /// </summary>
    public class APIResponse
    {
        /// <summary>
        /// Gets or sets the subject line for the generated content.
        /// </summary>
        [JsonProperty("subject_line")]
        public string? SubjectLine { get; set; }

        /// <summary>
        /// Gets or sets the headline for the generated content.
        /// </summary>
        [JsonProperty("headline")]
        public string? Headline { get; set; }

        /// <summary>
        /// Gets or sets the introductory text for the generated content.
        /// </summary>
        [JsonProperty("intro_paragraph")]
        public string? IntroText { get; set; }

        /// <summary>
        /// Gets or sets the call-to-action text for the generated content.
        /// </summary>
        [JsonProperty("call_to_action")]
        public string? CTAText { get; set; }

        /// <summary>
        /// Gets or sets the outro text for the generated content.
        /// </summary>
        [JsonProperty("outro_paragraph")]
        public string? OutroText { get; set; }
    }
}