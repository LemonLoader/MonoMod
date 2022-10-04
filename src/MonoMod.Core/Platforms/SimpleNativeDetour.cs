﻿using System;
using System.Runtime.InteropServices;

namespace MonoMod.Core.Platforms {
    public sealed class SimpleNativeDetour : IDisposable {
        private bool disposedValue;
        private readonly PlatformTriple triple;
        private readonly IDisposable? AllocHandle;

        public ReadOnlyMemory<byte> DetourBackup { get; }
        public IntPtr Source { get; }
        public IntPtr Destination { get; }
        public bool IsAutoUndone { get; private set; } = true;

        internal SimpleNativeDetour(PlatformTriple triple, IntPtr src, IntPtr dest, ReadOnlyMemory<byte> backup, IDisposable? allocHandle) {
            this.triple = triple;
            Source = src;
            Destination = dest;
            DetourBackup = backup;
            AllocHandle = allocHandle;
        }

        public void MakeManualOnly() {
            CheckDisposed();
            IsAutoUndone = false;
        }

        public void MakeAutomatic() {
            CheckDisposed();
            IsAutoUndone = true;
        }

        /// <summary>
        /// Undoes this detour. After this is called, the object is disposed, and may not be used.
        /// </summary>
        public void Undo() {
            CheckDisposed();
            UndoCore(true);
        }

        private void CheckDisposed() {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(SimpleNativeDetour));
        }

        private void UndoCore(bool disposing) {
            // literally just patch again, but the other direction
            triple.System.PatchData(PatchTargetKind.Executable, Source, DetourBackup.Span, default);
            if (disposing) {
                AllocHandle?.Dispose();
            }
            disposedValue = true;
        }

        private void Dispose(bool disposing) {
            if (!disposedValue) {
                if (IsAutoUndone) {
                    UndoCore(disposing);
                } else {
                    // create a gc handle to the allocHandle
                    _ = GCHandle.Alloc(AllocHandle);
                }

                disposedValue = true;
            }
        }

        ~SimpleNativeDetour() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}