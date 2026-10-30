using System.Runtime.InteropServices;

namespace MotionLinkVR.App;

public static class MouseDriver
{
    private const int InputMouse = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    private const uint MouseeventfMove = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref Input pInputs, int cbSize);

    public static void MoveRelative(int dx, int dy)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var input = new Input
        {
            Type = InputMouse,
            U = new InputUnion
            {
                Mi = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = 0,
                    DwFlags = MouseeventfMove,
                    Time = 0,
                    DwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, ref input, Marshal.SizeOf<Input>());
    }
}
