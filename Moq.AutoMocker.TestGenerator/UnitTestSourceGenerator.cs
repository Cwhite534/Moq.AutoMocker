﻿using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Moq.AutoMocker.TestGenerator;

[Generator]
public class UnitTestSourceGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.Compilation.Language is not LanguageNames.CSharp) return;

        if (!context.Compilation.ReferencedAssemblyNames.Any(ai => ai.Name.Equals(AutoMock.AssemblyName, StringComparison.OrdinalIgnoreCase)))
        {
            context.ReportDiagnostic(Diagnostics.MustReferenceAutoMock.Create());
            return;
        }

        SyntaxReceiver rx = (SyntaxReceiver)context.SyntaxContextReceiver!;

        foreach (Diagnostic diagnostic in rx.DiagnosticMessages)
        {
            context.ReportDiagnostic(diagnostic);
        }

        var testingFramework = GetTestingFramework(context.Compilation.ReferencedAssemblyNames);

        foreach (GeneratorTargetClass testClass in rx.TestClasses)
        {
            StringBuilder builder = new();

            builder.AppendLine($"namespace {testClass.Namespace}");
            builder.AppendLine("{");

            builder.AppendLine($"    partial class {testClass.TestClassName}");
            builder.AppendLine("    {");
            builder.AppendLine($"        partial void AutoMockerTestSetup(Moq.AutoMock.AutoMocker mocker, string testName);");
            builder.AppendLine();

            HashSet<string> testNames = new();

            foreach (var test in testClass.Sut?.NullConstructorParameterTests ?? Enumerable.Empty<NullConstructorParameterTest>())
            {
                string testName = "";
                foreach(var name in TestNameBuilder.CreateTestName(testClass, test))
                {
                    if (testNames.Add(name))
                    {
                        testName = name;
                        break;
                    }
                }

                builder.AppendLine($"        partial void {testName}Setup(Moq.AutoMock.AutoMocker mocker);");
                builder.AppendLine();

                switch (testingFramework)
                {
                    case TargetTestingFramework.MSTest:
                        builder.AppendLine("        [global::Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]");
                        break;
                    case TargetTestingFramework.Xunit:
                        builder.AppendLine("        [global::Xunit.Fact]");
                        break;
                    case TargetTestingFramework.NUnit:
                        builder.AppendLine("        [global::NUnit.Framework.Test]");
                        break;
                }


                builder.AppendLine($"        public void {testName}()");
                builder.AppendLine("        {");
                builder.AppendLine("            Moq.AutoMock.AutoMocker mocker = new Moq.AutoMock.AutoMocker();");
                builder.AppendLine($"            AutoMockerTestSetup(mocker, \"{testName}\");");
                builder.AppendLine($"            {testName}Setup(mocker);");

                for (int i = 0; i < test.Parameters?.Count; i++)
                {
                    if (i == test.NullParameterIndex) continue;
                    Parameter parameter = test.Parameters[i];
                    builder.AppendLine($"            var {parameter.Name} = mocker.Get<{parameter.ParameterType}>();");
                }

                string constructorInvocation = $"_ = new {testClass.Sut!.FullName}({string.Join(",", GetParameterNames(test))})";

                switch (testingFramework)
                {
                    case TargetTestingFramework.MSTest:
                        builder.AppendLine($"            System.ArgumentNullException ex = global::Microsoft.VisualStudio.TestTools.UnitTesting.Assert.ThrowsException<System.ArgumentNullException>(() => {constructorInvocation});");
                        builder.AppendLine($"            global::Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(\"{test.NullParameterName}\", ex.ParamName);");
                        break;
                    case TargetTestingFramework.Xunit:
                        builder.AppendLine($"            System.ArgumentNullException ex = global::Xunit.Assert.Throws<System.ArgumentNullException>(() => {constructorInvocation});");
                        builder.AppendLine($"            global::Xunit.Assert.Equal(\"{test.NullParameterName}\", ex.ParamName);");
                        break;
                    case TargetTestingFramework.NUnit:
                        builder.AppendLine($"            System.ArgumentNullException ex = global::NUnit.Framework.Assert.Throws<System.ArgumentNullException>(() => {constructorInvocation});");
                        builder.AppendLine($"            global::NUnit.Framework.Assert.AreEqual(\"{test.NullParameterName}\", ex.ParamName);");
                        break;
                }

                builder.AppendLine("        }");
                builder.AppendLine();
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            context.AddSource($"{testClass.TestClassName}.g.cs", builder.ToString());

        }

        static IEnumerable<string> GetParameterNames(NullConstructorParameterTest test)
        {
            for (int i = 0; i < test.Parameters?.Count; i++)
            {
                yield return i == test.NullParameterIndex 
                    ? $"default({test.Parameters[i].ParameterType})"
                    : test.Parameters[i].Name;
            }
        }
    }

    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        if (!System.Diagnostics.Debugger.IsAttached)
        {
            //System.Diagnostics.Debugger.Launch();
        }
#endif
        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    private static TargetTestingFramework GetTestingFramework(IEnumerable<AssemblyIdentity> assemblies)
    {
        foreach (AssemblyIdentity assembly in assemblies)
        {
            if (assembly.Name.StartsWith("Microsoft.VisualStudio.TestPlatform.TestFramework"))
            {
                return TargetTestingFramework.MSTest;
            }
            if (assembly.Name.StartsWith("nunit."))
            {
                return TargetTestingFramework.NUnit;
            }
            if (assembly.Name.StartsWith("xunit."))
            {
                return TargetTestingFramework.Xunit;
            }
        }
        return TargetTestingFramework.Unknown;
    }
}
