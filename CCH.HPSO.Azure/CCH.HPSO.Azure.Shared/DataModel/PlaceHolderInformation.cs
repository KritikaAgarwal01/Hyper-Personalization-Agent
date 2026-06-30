using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCH.HPSO.Azure.Shared.DataModel
{
    /// <summary>
    /// This class represents information about a placeholder.
    /// </summary>
    public class PlaceHolderInformation
    {
        /// <summary>
        /// The placeholder name
        /// </summary>
        public string? Placeholder { get; set; }

        /// <summary>
        /// The path to the placeholder in dataverse
        /// </summary>
        public string? TraversalPath { get; set; }

        /// <summary>
        /// The expected value of the placeholder
        /// </summary>
        public string? ActualValue { get; set; }
    }
}
