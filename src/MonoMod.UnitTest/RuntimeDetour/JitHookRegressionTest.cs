﻿#pragma warning disable CS1720 // Expression will always cause a System.NullReferenceException because the type's default value is null
#pragma warning disable xUnit1013 // Public method should be marked as test

using Xunit;
using System;
using System.Reflection;
using MonoMod.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MC = Mono.Cecil;
using MonoMod.Core.Platforms;
using Xunit.Abstractions;

namespace MonoMod.UnitTest {
    [Collection("RuntimeDetour")]
    public class JitHookRegressionTest : TestBase {

        static int ID;

        public JitHookRegressionTest(ITestOutputHelper helper) : base(helper) {
        }

        // At the time of writing, this should only affect .NET Core, but let's test almost all runtimes.
        [Fact]
        public void TestJitHookMissingMethod() {
            // ... except for .NET Core on Linux. Doesn't pass locally at all, passes on Azure only with 5.0.
#if !NETFRAMEWORK
            if (PlatformDetection.OS.Is(OSKind.Linux))
                return;
#endif

            // The JIT hook might already be set up thanks to previous tests.
            TestJitHookMissingMethodStep();

            Assert.NotNull(PlatformTriple.Current);

            // The JIT hook is definitely applied at this point.
            TestJitHookMissingMethodStep();
        }

        private void TestJitHookMissingMethodStep() {
            var id = ID++;
            var @namespace = "MonoMod.UnitTest";
            var @name = "JitHookRegressionTestHelper" + id;
            var @fullName = @namespace + "." + @name;

            Assembly asm;

            using (var module = ModuleDefinition.CreateModule(@fullName, new ModuleParameters() {
                Kind = ModuleKind.Dll,
                ReflectionImporterProvider = MMReflectionImporter.Provider
            })) {
                var type = new TypeDefinition(
                    @namespace,
                    @name,
                    MC.TypeAttributes.Public | MC.TypeAttributes.Abstract | MC.TypeAttributes.Sealed
                ) {
                    BaseType = module.TypeSystem.Object
                };
                module.Types.Add(type);

                var method = new MethodDefinition(@name,
                    MC.MethodAttributes.Public | MC.MethodAttributes.Static | MC.MethodAttributes.HideBySig,
                    module.TypeSystem.Void
                );
                type.Methods.Add(method);

                ILProcessor il = method.Body.GetILProcessor();
                il.Emit(OpCodes.Call, module.ImportReference(
                    new MethodReference(
                        "MissingMethod" + id,
                        module.TypeSystem.Void,
                        new TypeReference(
                            "TotallyNotReal", "MissingType",
                            module,
                            new AssemblyNameReference("TotallyNotReal", new Version(0, 0, 0, 0))
                        )
                    )
                ));
                il.Emit(OpCodes.Ret);

                asm = ReflectionHelper.Load(module);
            }

            try {
                (asm.GetType(@fullName).GetMethod(@name).CreateDelegate<Action>())();
            } catch (TypeLoadException) {
            }
        }

    }
}
