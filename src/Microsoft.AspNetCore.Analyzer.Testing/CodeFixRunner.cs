﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Microsoft.AspNetCore.Analyzer.Testing
{
    public class CodeFixRunner
    {
        public static CodeFixRunner Default { get; } = new CodeFixRunner();

        public async Task<Solution> GetChangedSolutionAsync(
            CodeFixProvider codeFixProvider,
            Document document,
            Diagnostic analyzerDiagnostic,
            int codeFixIndex = 0)
        {
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, analyzerDiagnostic, (a, d) => actions.Add(a), CancellationToken.None);
            await codeFixProvider.RegisterCodeFixesAsync(context);

            Assert.NotEmpty(actions);

            return await ApplyFixAsync(actions[codeFixIndex]);
        }

        public async Task<string> ApplyCodeFixAsync(
            CodeFixProvider codeFixProvider,
            Document document,
            Diagnostic analyzerDiagnostic,
            int codeFixIndex = 0)
        {
            var updatedSolution = await GetChangedSolutionAsync(codeFixProvider, document, analyzerDiagnostic, codeFixIndex);

            var project = updatedSolution.GetProject(document.Project.Id);
            await EnsureCompilable(project);

            var updatedDocument = updatedSolution.GetDocument(document.Id);
            var sourceText = await updatedDocument.GetTextAsync();
            return sourceText.ToString();
        }

        public async Task EnsureCompilable(Project project)
        {
            var compilationOptions = ConfigureCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compilation = await project
                .WithCompilationOptions(compilationOptions)
                .GetCompilationAsync();
            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Length != 0)
            {
                var message = string.Join(
                    Environment.NewLine,
                    diagnostics.Select(d => CSharpDiagnosticFormatter.Instance.Format(d)));
                throw new InvalidOperationException($"Compilation failed:{Environment.NewLine}{message}");
            }
        }

        private static async Task<Solution> ApplyFixAsync(CodeAction codeAction)
        {
            var operations = await codeAction.GetOperationsAsync(CancellationToken.None);
            return Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;
        }

        protected virtual CompilationOptions ConfigureCompilationOptions(CompilationOptions options)
        {
            return options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary);
        }
    }
}
