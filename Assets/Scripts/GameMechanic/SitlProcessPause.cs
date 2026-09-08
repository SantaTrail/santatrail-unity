using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

// Only receives the process started by SantaTrail. Never discovers or suspends
// another app's simulator, QGroundControl, or the Unity process itself.
public sealed class SitlProcessPause : IDisposable
{
    private readonly Process process;
    private readonly Dictionary<int, IntPtr> suspendedThreads = new Dictionary<int, IntPtr>();
    public bool IsPaused { get; private set; }

    public SitlProcessPause(Process ownedProcess)
    {
        process = ownedProcess;
    }

    public void SetPaused(bool paused)
    {
        if (paused == IsPaused) return;
        if (process.HasExited) throw new InvalidOperationException("The flight simulator has exited.");

        bool windows = Application.platform == RuntimePlatform.WindowsEditor ||
            Application.platform == RuntimePlatform.WindowsPlayer;
        if (windows)
        {
            if (paused)
            {
                IsPaused = true;
                try
                {
                    // Repeat the snapshot to include threads created while the
                    // previous snapshot was being suspended.
                    for (int pass = 0; pass < 8; pass++)
                    {
                        process.Refresh();
                        bool added = false;
                        foreach (ProcessThread thread in process.Threads)
                        {
                            if (suspendedThreads.ContainsKey(thread.Id)) continue;
                            IntPtr handle = OpenThread(0x0002, false, (uint)thread.Id);
                            if (handle == IntPtr.Zero) throw new Win32Exception();
                            if (SuspendThread(handle) == uint.MaxValue)
                            {
                                int error = Marshal.GetLastWin32Error();
                                CloseHandle(handle);
                                throw new Win32Exception(error);
                            }
                            suspendedThreads.Add(thread.Id, handle);
                            added = true;
                        }
                        if (!added)
                        {
                            IsPaused = true;
                            return;
                        }
                    }
                    throw new InvalidOperationException("The simulator threads could not be paused.");
                }
                catch
                {
                    ResumeWindowsThreads();
                    IsPaused = false;
                    throw;
                }
            }
            ResumeWindowsThreads();
        }
        else if (Application.platform == RuntimePlatform.OSXEditor ||
                 Application.platform == RuntimePlatform.OSXPlayer)
        {
            // Darwin SIGSTOP=17 and SIGCONT=19. The launch script execs SITL,
            // so its PID is also the simulator PID.
            if (kill(process.Id, paused ? 17 : 19) != 0) throw new Win32Exception();
        }
        else
        {
            throw new PlatformNotSupportedException("Simulator pause is supported on macOS and Windows.");
        }
        IsPaused = paused;
    }

    private void ResumeWindowsThreads()
    {
        int error = 0;
        foreach (int id in new List<int>(suspendedThreads.Keys))
        {
            IntPtr handle = suspendedThreads[id];
            if (ResumeThread(handle) == uint.MaxValue && !process.HasExited)
            {
                error = Marshal.GetLastWin32Error();
                continue;
            }
            CloseHandle(handle);
            suspendedThreads.Remove(id);
        }
        if (error != 0) throw new Win32Exception(error);
    }

    public void Dispose()
    {
        try
        {
            if (IsPaused && !process.HasExited) SetPaused(false);
        }
        finally
        {
            foreach (IntPtr handle in suspendedThreads.Values) CloseHandle(handle);
            suspendedThreads.Clear();
            IsPaused = false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);
}
