﻿using System;

namespace MonoMod.Core.Platforms.Architectures {

    internal static class x86Shared {
        public sealed class Rel32Kind : DetourKindBase {
            public static readonly Rel32Kind Instance = new();

            public override int Size => 1 + 4;

            public override int GetBytes(IntPtr from, IntPtr to, Span<byte> buffer, object? data, out IDisposable? allocHandle) {
                buffer[0] = 0xe9;
                Unsafe.WriteUnaligned(ref buffer[1], (int) (to - ((nint) from + 5)));
                allocHandle = null;
                return Size;
            }
        }

        public static void FixSizeHint(ref int sizeHint) {
            if (sizeHint < 0) {
                sizeHint = int.MaxValue;
            }
        }

        public static bool TryRel32Detour(nint from, nint to, int sizeHint, out NativeDetourInfo info) {
            var rel = to - (from + 5);

            if (sizeHint >= Rel32Kind.Instance.Size && (Is32Bit(rel) || Is32Bit(-rel))) {
                unsafe {
                    if (*((byte*) from + 5) != 0x5f) {
                        // because Rel32 uses an E9 jump, the byte that would be immediately following the jump
                        //   must not be 0x5f, otherwise it would be picked up by the matcher on line 44 of x86_64Arch
                        info = new(from, to, Rel32Kind.Instance, null);
                        return true;
                    }
                }
            }

            info = default;
            return false;
        }

        public static bool Is32Bit(long to)
            // JMP rel32 is "sign extended to 64-bits"
            => (((ulong) to) & 0x000000007FFFFFFFUL) == ((ulong) to);
    }
}
