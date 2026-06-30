using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCH.HPSO.Azure.Shared.Enum
{
    public enum FailureStageEnum
    {
        /// <summary>
        /// The failure stage is not set.
        /// </summary>
        None = 0,

        /// <summary>
        /// The failure stage is during the prompt generation phase.
        /// </summary>
        PromptGeneration = 1,

        /// <summary>
        /// The failure stage is during the text generation phase.
        /// </summary>
        TextGeneration = 2,

        /// <summary>
        /// The failure stage is during the evaluation and publishing phase.
        /// </summary>
        EvaluationAndPublish = 3
    }
}
