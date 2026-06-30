using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCH.HPSO.Azure.Shared.Contracts
{
    /// <summary>
    /// The IEvaluationApiService interface defines a contract for a service that calls another Azure Function via HTTP POST.
    /// </summary>
    public interface IEvaluationApiService
    {
        /// <summary>
        /// Calls another Azure Function via HTTP POST.
        /// </summary>
        /// <param name="payload">The object to send as JSON.</param>
        /// <returns>The response string from the called function.</returns>
        Task<string> CallEvaluationApi(object payload);
    }
}
