using Microsoft.CodeAnalysis;
using SoftwareParticles.DotNetEngine;
using SoftwareParticles.DynamicScriptExecution.Core;
using SoftwareParticles.DynamicScriptExecution.Core.Errors;
using SoftwareParticles.DynamicScriptExecution.Core.Results;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using ParameterDirection = SoftwareParticles.DynamicScriptExecution.Core.ParameterDirection;

namespace SoftwareParticles.DynamicScriptExecution.DotNet
{
    public abstract class DotNetDynamicScriptController : IDynamicScriptController<DotNetDynamicScriptParameter, DotNetCallArguments>
    {
        private Assembly _assembly;
        private DotNetDynamicScriptParameter _operationParams;

        private readonly CodeTemplate _codeTemplate;

        protected DotNetDynamicScriptController(CodeTemplate codeTemplate)
        {
            _codeTemplate = codeTemplate;
        }

        public IEnumerable<ParameterDefinition> OutputParameters
            => _operationParams.Parameters.Where(x => x.Direction == ParameterDirection.Output
                || x.Direction == ParameterDirection.InputOutput);

        public EvaluationResult Evaluate(DotNetDynamicScriptParameter p)
        {
            _operationParams = p;

            var code = _codeTemplate.Build(p);

            var syntaxTree = GetSyntaxTree(code);

            var compilerRootPath = Path.GetDirectoryName(typeof(object).Assembly.Location) + Path.DirectorySeparatorChar;

            // NOTE - this needs to be the path to netstandard2.0 package location,
            // TODO: find a better way to ref
            var referenceRootPath = @"C:\Users\simon.SPINN\.nuget\packages\netstandard.library\2.0.3\build\netstandard2.0\ref\";
            
            var compilation = GetCompilationForAssembly(p.AssemblyName)
                .WithOptions(GetOptions())
                .AddSyntaxTrees(syntaxTree);
            
            compilation = AddDefaultReferences(referenceRootPath, compilation);
            compilation = AddReferences(compilerRootPath, compilation);

            foreach (var reference in p.References)
            {
                compilation = compilation.AddReferences(MetadataReference.CreateFromFile(reference));
            }

            var diagnostics = compilation.GetDiagnostics();

            var returnValue = EvaluationResult.Ok(); 
            
            var errors = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error);
            if (errors.Any())
            {
                int lineOffset = _codeTemplate.GetCodeLineOffset(code);
                returnValue = EvaluationResult.WithErrors(errors.Select(x => GetDynamicScriptError(x, lineOffset)));
            }
            
            var warnings = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Warning);
            if (warnings.Any())
            {
                int lineOffset = _codeTemplate.GetCodeLineOffset(code);
                returnValue.Warnings = warnings.Select(x => GetDynamicScriptError(x, lineOffset));
            }

            if (!returnValue.Success)
            {
                return returnValue;
            }

            //using (var fs = File.OpenWrite("GeneratedAssembly.dll"))
            
            using (var ms = new MemoryStream())
            {
                string resourcesFileLocation = Path.Combine(Path.GetDirectoryName(this.GetType().Assembly.Location),
                    "resources.resx");

                var result = compilation.Emit(ms, pdbStream: null);

                if (!result.Success)
                {
                    int lineOffset = _codeTemplate.GetCodeLineOffset(code);
                    return EvaluationResult.WithErrors(result.Diagnostics
                        .Where(x => x.Severity == DiagnosticSeverity.Error)
                        .Select(x => GetDynamicScriptError(x, lineOffset)));
                }

                var assemblyBytes = ms.ToArray();
                _assembly = Assembly.Load(assemblyBytes);

                // TODO: Output the assembly for usage
                // ms.Position = 0;
                // ms.CopyTo(fs);
            }

            return returnValue;
        }

        private DynamicScriptCompilationError GetDynamicScriptError(Diagnostic d, int lineOffset)
        {
            var error = new DynamicScriptCompilationError(d.GetMessage(),
                d.Location.GetLineSpan().StartLinePosition.Line - lineOffset,
                d.Location.GetLineSpan().EndLinePosition.Line - lineOffset,
                d.Location.SourceSpan.Start,
                d.Location.SourceSpan.Length);
            return error;
        }

        public ExecutionResult Execute(DotNetCallArguments callArgs = null, List<ParameterArgument> methodArgs = null)
        {
            try
            {
                var instanceResult = _codeTemplate.CreateInstance(callArgs ?? new DotNetCallArguments(), _assembly);

                var arguments = ParseMethodArguments(methodArgs, instanceResult.Method);
                var methodResult = instanceResult.Method.Invoke(instanceResult.Instance, arguments);

                var res = ExecutionResult.Ok();
                if(instanceResult.Method.ReturnType != typeof(void))
                    res.Add(new MethodReturnValue(methodResult));
                
                foreach (var outParam in OutputParameters)
                {
                    var entry = methodArgs.FirstOrDefault(x => x.Key == outParam.Key);
                    var index = _operationParams.Parameters.IndexOf(outParam);

                    res.Add(new ExecutionResultEntry(outParam.Key, arguments[index]));
                }

                return res;
            }
            catch (TargetInvocationException tie)
            {
                return ExecutionResult.WithError(tie);
            }
            catch (ArgumentException ae)
            {
                return ExecutionResult.WithError(ae);
            }
        }

        private object[] ParseMethodArguments(List<ParameterArgument> methodArgs, MethodInfo method)
        {
            List<object> arguments = new List<object>();
            foreach(var parameter in method.GetParameters())
            {
                var arg = methodArgs.FirstOrDefault(x => x.Key == parameter.Name);
                arguments.Add(arg?.Value);
            }
            return arguments.ToArray();
        }

        protected abstract Compilation GetCompilationForAssembly(string assemblyName);

        protected abstract CompilationOptions GetOptions();

        protected abstract SyntaxTree GetSyntaxTree(string code);

        private Compilation AddDefaultReferences(string rootPath, Compilation compilation)
        {
            return compilation
                // TODO: These will reference non-netstandard assemblies, fine if running generated code in same env
                // .AddReferences(MetadataReference.CreateFromFile(typeof(int).Assembly.Location))
                // .AddReferences(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location))
                // .AddReferences(MetadataReference.CreateFromFile(typeof(DataTable).Assembly.Location))
                // .AddReferences(MetadataReference.CreateFromFile(typeof(Object).Assembly.Location))
                // .AddReferences(MetadataReference.CreateFromFile(typeof(File).Assembly.Location))
                // .AddReferences(MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location))
                // .AddReferences(MetadataReference.CreateFromFile(typeof(AssemblyTitleAttribute).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(Path.Combine(rootPath, "System.dll")))
                .AddReferences(MetadataReference.CreateFromFile(Path.Combine(rootPath, "netstandard.dll")))
                .AddReferences(MetadataReference.CreateFromFile(Path.Combine(rootPath, "System.Runtime.dll")));
        }

        protected virtual Compilation AddReferences(string rootPath, Compilation compilation)
            => compilation;
    }
}
