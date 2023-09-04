using SoftwareParticles.DynamicScriptExecution.Core.Errors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SoftwareParticles.DynamicScriptExecution.Core.Results
{
    public class EvaluationResult
    {
        public bool Success => !Errors.Any();
        public IEnumerable<DynamicScriptCompilationError> Errors { get; }
        public IEnumerable<DynamicScriptCompilationError> Warnings { get; set; } = new List<DynamicScriptCompilationError>();

        private EvaluationResult(IEnumerable<DynamicScriptCompilationError> errors = null, IEnumerable<DynamicScriptCompilationError> warnings = null)
        {
            Errors = errors ?? new List<DynamicScriptCompilationError>();
            Warnings = warnings ?? new List<DynamicScriptCompilationError>();
        }

        public static EvaluationResult WithErrors(IEnumerable<DynamicScriptCompilationError> errors)
            => new EvaluationResult(errors);
        
        public static EvaluationResult Ok()
            => new EvaluationResult();
    }
}
