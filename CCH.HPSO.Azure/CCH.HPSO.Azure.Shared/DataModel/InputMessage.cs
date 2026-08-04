using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCH.HPSO.Azure.Shared.DataModel
{
    public class InputMessage
    {
        /// <summary>
        /// Gets or sets the Contact Id 
        /// </summary>
        public string? ContactId { get; set; }
        
        /// <summary>
        /// Gets or sets the Prompt Text
        /// </summary>
        public string? PromptText { get; set; }

        /// <summary>
        /// Gets or sets the Compliance Threshold
        /// </summary>
        public string? ComplianceThreshold { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the feature is in preview mode.
        /// </summary>
        public string? IsPreview { get; set; }

        /// <summary>
        /// Gets or sets the Prompt Template Id
        /// </summary>
        public string? PromptTemplateId { get; set; }

        /// <summary>
        /// Gets or sets the Prompt Template name
        /// </summary>
        public string? PromptTemplateName { get; set; }
        
        /// <summary>
        /// Gets or sets the Contact Name
        /// </summary>
        public string? ContactName { get; set; }

        /// <summary>
        /// Gets or sets the prompt language
        /// </summary>
        public string? PromptLanguage { get; set; }

        /// <summary>
        /// Gets or sets the tone
        /// </summary>
        public string? Tone { get; set; }

        /// <summary>
        /// Gets or sets the deployment name for the prompt
        /// </summary>
        public string? PromptDeploymentName { get; set; }
        
        /// <summary>
        /// Gets or sets a property
        /// </summary>
        public string? PromptAppVersion { get; set; }

        /// <summary>
        /// Gets or sets the configurable OpenAI system message fetched from the prompt template in Dataverse.
        /// May contain the {PromptLanguage} and {Tone} tokens, which are substituted before the call to Azure OpenAI.
        /// </summary>
        public string? SystemMessage { get; set; }
    }
}