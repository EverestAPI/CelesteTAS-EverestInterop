using System;
using Eto.Forms;

namespace CelesteStudio.Util;

// from WinForms / XNA
public enum WinFormsKeys {
    /// <summary><para>No key pressed.</para></summary>
    None = 0,
    /// <summary><para>The left mouse button.</para></summary>
    LButton = 1,
    /// <summary><para>The right mouse button.</para></summary>
    RButton = 2,
    /// <summary><para>The CANCEL key.</para></summary>
    Cancel = RButton | LButton, // 0x00000003
    /// <summary><para>The middle mouse button (three-button mouse).</para></summary>
    MButton = 4,
    /// <summary><para>The first x mouse button (five-button mouse).</para></summary>
    XButton1 = MButton | LButton, // 0x00000005
    /// <summary><para>The second x mouse button (five-button mouse).</para></summary>
    XButton2 = MButton | RButton, // 0x00000006
    /// <summary><para>The BACKSPACE key.</para></summary>
    Back = 8,
    /// <summary><para>The TAB key.</para></summary>
    Tab = Back | LButton, // 0x00000009
    /// <summary><para>The LINEFEED key.</para></summary>
    LineFeed = Back | RButton, // 0x0000000A
    /// <summary><para>The CLEAR key.</para></summary>
    Clear = Back | MButton, // 0x0000000C
    /// <summary><para>The RETURN key.</para></summary>
    Return = Clear | LButton, // 0x0000000D
    /// <summary><para>The ENTER key.</para></summary>
    Enter = Return, // 0x0000000D
    /// <summary><para>The SHIFT key.</para></summary>
    ShiftKey = 16, // 0x00000010
    /// <summary><para>The CTRL key.</para></summary>
    ControlKey = ShiftKey | LButton, // 0x00000011
    /// <summary><para>The ALT key.</para></summary>
    Menu = ShiftKey | RButton, // 0x00000012
    /// <summary><para>The PAUSE key.</para></summary>
    Pause = Menu | LButton, // 0x00000013
    /// <summary><para>The CAPS LOCK key.</para></summary>
    CapsLock = ShiftKey | MButton, // 0x00000014
    /// <summary><para>The CAPS LOCK key.</para></summary>
    Capital = CapsLock, // 0x00000014
    /// <summary><para>The IME Kana mode key.</para></summary>
    KanaMode = Capital | LButton, // 0x00000015
    /// <summary><para>The IME Hanguel mode key. (maintained for compatibility; use HangulMode) </para></summary>
    HanguelMode = KanaMode, // 0x00000015
    /// <summary><para>The IME Hangul mode key.</para></summary>
    HangulMode = HanguelMode, // 0x00000015
    /// <summary><para>The IME Junja mode key.</para></summary>
    JunjaMode = HangulMode | RButton, // 0x00000017
    /// <summary><para>The IME final mode key.</para></summary>
    FinalMode = ShiftKey | Back, // 0x00000018
    /// <summary><para>The IME Kanji mode key.</para></summary>
    KanjiMode = FinalMode | LButton, // 0x00000019
    /// <summary><para>The IME Hanja mode key.</para></summary>
    HanjaMode = KanjiMode, // 0x00000019
    /// <summary><para>The ESC key.</para></summary>
    Escape = HanjaMode | RButton, // 0x0000001B
    /// <summary><para>The IME convert key.</para></summary>
    IMEConvert = FinalMode | MButton, // 0x0000001C
    /// <summary><para>The IME nonconvert key.</para></summary>
    IMENonconvert = IMEConvert | LButton, // 0x0000001D
    /// <summary><para>The IME accept key. Obsolete, use <see cref="F:System.Windows.Forms.Keys.IMEAccept" /> instead.</para></summary>
    IMEAceept = IMEConvert | RButton, // 0x0000001E
    /// <summary><para>The IME mode change key.</para></summary>
    IMEModeChange = IMEAceept | LButton, // 0x0000001F
    /// <summary><para>The SPACEBAR key.</para></summary>
    Space = 32, // 0x00000020
    /// <summary><para>The PAGE UP key.</para></summary>
    PageUp = Space | LButton, // 0x00000021
    /// <summary><para>The PAGE UP key.</para></summary>
    Prior = PageUp, // 0x00000021
    /// <summary><para>The PAGE DOWN key.</para></summary>
    PageDown = Space | RButton, // 0x00000022
    /// <summary><para>The PAGE DOWN key.</para></summary>
    Next = PageDown, // 0x00000022
    /// <summary><para>The END key.</para></summary>
    End = Next | LButton, // 0x00000023
    /// <summary><para>The HOME key.</para></summary>
    Home = Space | MButton, // 0x00000024
    /// <summary><para>The LEFT ARROW key.</para></summary>
    Left = Home | LButton, // 0x00000025
    /// <summary><para>The UP ARROW key.</para></summary>
    Up = Home | RButton, // 0x00000026
    /// <summary><para>The RIGHT ARROW key.</para></summary>
    Right = Up | LButton, // 0x00000027
    /// <summary><para>The DOWN ARROW key.</para></summary>
    Down = Space | Back, // 0x00000028
    /// <summary><para>The SELECT key.</para></summary>
    Select = Down | LButton, // 0x00000029
    /// <summary><para>The PRINT key.</para></summary>
    Print = Down | RButton, // 0x0000002A
    /// <summary><para>The EXECUTE key.</para></summary>
    Execute = Print | LButton, // 0x0000002B
    /// <summary><para>The PRINT SCREEN key.</para></summary>
    PrintScreen = Down | MButton, // 0x0000002C
    /// <summary><para>The PRINT SCREEN key.</para></summary>
    Snapshot = PrintScreen, // 0x0000002C
    /// <summary><para>The INS key.</para></summary>
    Insert = Snapshot | LButton, // 0x0000002D
    /// <summary><para>The DEL key.</para></summary>
    Delete = Snapshot | RButton, // 0x0000002E
    /// <summary><para>The HELP key.</para></summary>
    Help = Delete | LButton, // 0x0000002F
    /// <summary><para>The 0 key.</para></summary>
    D0 = Space | ShiftKey, // 0x00000030
    /// <summary><para>The 1 key.</para></summary>
    D1 = D0 | LButton, // 0x00000031
    /// <summary><para>The 2 key.</para></summary>
    D2 = D0 | RButton, // 0x00000032
    /// <summary><para>The 3 key.</para></summary>
    D3 = D2 | LButton, // 0x00000033
    /// <summary><para>The 4 key.</para></summary>
    D4 = D0 | MButton, // 0x00000034
    /// <summary><para>The 5 key.</para></summary>
    D5 = D4 | LButton, // 0x00000035
    /// <summary><para>The 6 key.</para></summary>
    D6 = D4 | RButton, // 0x00000036
    /// <summary><para>The 7 key.</para></summary>
    D7 = D6 | LButton, // 0x00000037
    /// <summary><para>The 8 key.</para></summary>
    D8 = D0 | Back, // 0x00000038
    /// <summary><para>The 9 key.</para></summary>
    D9 = D8 | LButton, // 0x00000039
    /// <summary><para>The A key.</para></summary>
    A = 65, // 0x00000041
    /// <summary><para>The B key.</para></summary>
    B = 66, // 0x00000042
    /// <summary><para>The C key.</para></summary>
    C = B | LButton, // 0x00000043
    /// <summary><para>The D key.</para></summary>
    D = 68, // 0x00000044
    /// <summary><para>The E key.</para></summary>
    E = D | LButton, // 0x00000045
    /// <summary><para>The F key.</para></summary>
    F = D | RButton, // 0x00000046
    /// <summary><para>The G key.</para></summary>
    G = F | LButton, // 0x00000047
    /// <summary><para>The H key.</para></summary>
    H = 72, // 0x00000048
    /// <summary><para>The I key.</para></summary>
    I = H | LButton, // 0x00000049
    /// <summary><para>The J key.</para></summary>
    J = H | RButton, // 0x0000004A
    /// <summary><para>The K key.</para></summary>
    K = J | LButton, // 0x0000004B
    /// <summary><para>The L key.</para></summary>
    L = H | MButton, // 0x0000004C
    /// <summary><para>The M key.</para></summary>
    M = L | LButton, // 0x0000004D
    /// <summary><para>The N key.</para></summary>
    N = L | RButton, // 0x0000004E
    /// <summary><para>The O key.</para></summary>
    O = N | LButton, // 0x0000004F
    /// <summary><para>The P key.</para></summary>
    P = 80, // 0x00000050
    /// <summary><para>The Q key.</para></summary>
    Q = P | LButton, // 0x00000051
    /// <summary><para>The R key.</para></summary>
    R = P | RButton, // 0x00000052
    /// <summary><para>The S key.</para></summary>
    S = R | LButton, // 0x00000053
    /// <summary><para>The T key.</para></summary>
    T = P | MButton, // 0x00000054
    /// <summary><para>The U key.</para></summary>
    U = T | LButton, // 0x00000055
    /// <summary><para>The V key.</para></summary>
    V = T | RButton, // 0x00000056
    /// <summary><para>The W key.</para></summary>
    W = V | LButton, // 0x00000057
    /// <summary><para>The X key.</para></summary>
    X = P | Back, // 0x00000058
    /// <summary><para>The Y key.</para></summary>
    Y = X | LButton, // 0x00000059
    /// <summary><para>The Z key.</para></summary>
    Z = X | RButton, // 0x0000005A
    /// <summary><para>The left Windows logo key (Microsoft Natural Keyboard).</para></summary>
    LWin = Z | LButton, // 0x0000005B
    /// <summary><para>The right Windows logo key (Microsoft Natural Keyboard).</para></summary>
    RWin = X | MButton, // 0x0000005C
    /// <summary><para>The application key (Microsoft Natural Keyboard).</para></summary>
    Apps = RWin | LButton, // 0x0000005D
    /// <summary><para>The 0 key on the numeric keypad.</para></summary>
    NumPad0 = 96, // 0x00000060
    /// <summary><para>The 1 key on the numeric keypad.</para></summary>
    NumPad1 = NumPad0 | LButton, // 0x00000061
    /// <summary><para>The 2 key on the numeric keypad.</para></summary>
    NumPad2 = NumPad0 | RButton, // 0x00000062
    /// <summary><para>The 3 key on the numeric keypad.</para></summary>
    NumPad3 = NumPad2 | LButton, // 0x00000063
    /// <summary><para>The 4 key on the numeric keypad.</para></summary>
    NumPad4 = NumPad0 | MButton, // 0x00000064
    /// <summary><para>The 5 key on the numeric keypad.</para></summary>
    NumPad5 = NumPad4 | LButton, // 0x00000065
    /// <summary><para>The 6 key on the numeric keypad.</para></summary>
    NumPad6 = NumPad4 | RButton, // 0x00000066
    /// <summary><para>The 7 key on the numeric keypad.</para></summary>
    NumPad7 = NumPad6 | LButton, // 0x00000067
    /// <summary><para>The 8 key on the numeric keypad.</para></summary>
    NumPad8 = NumPad0 | Back, // 0x00000068
    /// <summary><para>The 9 key on the numeric keypad.</para></summary>
    NumPad9 = NumPad8 | LButton, // 0x00000069
    /// <summary><para>The multiply key.</para></summary>
    Multiply = NumPad8 | RButton, // 0x0000006A
    /// <summary><para>The add key.</para></summary>
    Add = Multiply | LButton, // 0x0000006B
    /// <summary><para>The separator key.</para></summary>
    Separator = NumPad8 | MButton, // 0x0000006C
    /// <summary><para>The subtract key.</para></summary>
    Subtract = Separator | LButton, // 0x0000006D
    /// <summary><para>The decimal key.</para></summary>
    Decimal = Separator | RButton, // 0x0000006E
    /// <summary><para>The divide key.</para></summary>
    Divide = Decimal | LButton, // 0x0000006F
    /// <summary><para>The F1 key.</para></summary>
    F1 = NumPad0 | ShiftKey, // 0x00000070
    /// <summary><para>The F2 key.</para></summary>
    F2 = F1 | LButton, // 0x00000071
    /// <summary><para>The F3 key.</para></summary>
    F3 = F1 | RButton, // 0x00000072
    /// <summary><para>The F4 key.</para></summary>
    F4 = F3 | LButton, // 0x00000073
    /// <summary><para>The F5 key.</para></summary>
    F5 = F1 | MButton, // 0x00000074
    /// <summary><para>The F6 key.</para></summary>
    F6 = F5 | LButton, // 0x00000075
    /// <summary><para>The F7 key.</para></summary>
    F7 = F5 | RButton, // 0x00000076
    /// <summary><para>The F8 key.</para></summary>
    F8 = F7 | LButton, // 0x00000077
    /// <summary><para>The F9 key.</para></summary>
    F9 = F1 | Back, // 0x00000078
    /// <summary><para>The F10 key.</para></summary>
    F10 = F9 | LButton, // 0x00000079
    /// <summary><para>The F11 key.</para></summary>
    F11 = F9 | RButton, // 0x0000007A
    /// <summary><para>The F12 key.</para></summary>
    F12 = F11 | LButton, // 0x0000007B
    /// <summary><para>The F13 key.</para></summary>
    F13 = F9 | MButton, // 0x0000007C
    /// <summary><para>The F14 key.</para></summary>
    F14 = F13 | LButton, // 0x0000007D
    /// <summary><para>The F15 key.</para></summary>
    F15 = F13 | RButton, // 0x0000007E
    /// <summary><para>The F16 key.</para></summary>
    F16 = F15 | LButton, // 0x0000007F
    /// <summary><para>The F17 key.</para></summary>
    F17 = 128, // 0x00000080
    /// <summary><para>The F18 key.</para></summary>
    F18 = F17 | LButton, // 0x00000081
    /// <summary><para>The F19 key.</para></summary>
    F19 = F17 | RButton, // 0x00000082
    /// <summary><para>The F20 key.</para></summary>
    F20 = F19 | LButton, // 0x00000083
    /// <summary><para>The F21 key.</para></summary>
    F21 = F17 | MButton, // 0x00000084
    /// <summary><para>The F22 key.</para></summary>
    F22 = F21 | LButton, // 0x00000085
    /// <summary><para>The F23 key.</para></summary>
    F23 = F21 | RButton, // 0x00000086
    /// <summary><para>The F24 key.</para></summary>
    F24 = F23 | LButton, // 0x00000087
    /// <summary><para>The NUM LOCK key.</para></summary>
    NumLock = F17 | ShiftKey, // 0x00000090
    /// <summary><para>The SCROLL LOCK key.</para></summary>
    Scroll = NumLock | LButton, // 0x00000091
    /// <summary><para>The left SHIFT key.</para></summary>
    LShiftKey = F17 | Space, // 0x000000A0
    /// <summary><para>The right SHIFT key.</para></summary>
    RShiftKey = LShiftKey | LButton, // 0x000000A1
    /// <summary><para>The left CTRL key.</para></summary>
    LControlKey = LShiftKey | RButton, // 0x000000A2
    /// <summary><para>The right CTRL key.</para></summary>
    RControlKey = LControlKey | LButton, // 0x000000A3
    /// <summary><para>The left ALT key.</para></summary>
    LMenu = LShiftKey | MButton, // 0x000000A4
    /// <summary><para>The right ALT key.</para></summary>
    RMenu = LMenu | LButton, // 0x000000A5
    /// <summary><para>The browser back key (Windows 2000 or later).</para></summary>
    BrowserBack = LMenu | RButton, // 0x000000A6
    /// <summary><para>The browser forward key (Windows 2000 or later).</para></summary>
    BrowserForward = BrowserBack | LButton, // 0x000000A7
    /// <summary><para>The browser refresh key (Windows 2000 or later).</para></summary>
    BrowserRefresh = LShiftKey | Back, // 0x000000A8
    /// <summary><para>The browser stop key (Windows 2000 or later).</para></summary>
    BrowserStop = BrowserRefresh | LButton, // 0x000000A9
    /// <summary><para>The browser search key (Windows 2000 or later).</para></summary>
    BrowserSearch = BrowserRefresh | RButton, // 0x000000AA
    /// <summary><para>The browser favorites key (Windows 2000 or later).</para></summary>
    BrowserFavorites = BrowserSearch | LButton, // 0x000000AB
    /// <summary><para>The browser home key (Windows 2000 or later).</para></summary>
    BrowserHome = BrowserRefresh | MButton, // 0x000000AC
    /// <summary><para>The volume mute key (Windows 2000 or later).</para></summary>
    VolumeMute = BrowserHome | LButton, // 0x000000AD
    /// <summary><para>The volume down key (Windows 2000 or later).</para></summary>
    VolumeDown = BrowserHome | RButton, // 0x000000AE
    /// <summary><para>The volume up key (Windows 2000 or later).</para></summary>
    VolumeUp = VolumeDown | LButton, // 0x000000AF
    /// <summary><para>The media next track key (Windows 2000 or later).</para></summary>
    MediaNextTrack = LShiftKey | ShiftKey, // 0x000000B0
    /// <summary><para>The media previous track key (Windows 2000 or later).</para></summary>
    MediaPreviousTrack = MediaNextTrack | LButton, // 0x000000B1
    /// <summary><para>The media Stop key (Windows 2000 or later).</para></summary>
    MediaStop = MediaNextTrack | RButton, // 0x000000B2
    /// <summary><para>The media play pause key (Windows 2000 or later).</para></summary>
    MediaPlayPause = MediaStop | LButton, // 0x000000B3
    /// <summary><para>The launch mail key (Windows 2000 or later).</para></summary>
    LaunchMail = MediaNextTrack | MButton, // 0x000000B4
    /// <summary><para>The select media key (Windows 2000 or later).</para></summary>
    SelectMedia = LaunchMail | LButton, // 0x000000B5
    /// <summary><para>The start application one key (Windows 2000 or later).</para></summary>
    LaunchApplication1 = LaunchMail | RButton, // 0x000000B6
    /// <summary><para>The start application two key (Windows 2000 or later).</para></summary>
    LaunchApplication2 = LaunchApplication1 | LButton, // 0x000000B7
    /// <summary><para>The OEM Semicolon key on a US standard keyboard (Windows 2000 or later).</para></summary>
    OemSemicolon = MediaStop | Back, // 0x000000BA
    /// <summary><para>The OEM plus key on any country/region keyboard (Windows 2000 or later).</para></summary>
    Oemplus = OemSemicolon | LButton, // 0x000000BB
    /// <summary><para>The OEM comma key on any country/region keyboard (Windows 2000 or later).</para></summary>
    Oemcomma = LaunchMail | Back, // 0x000000BC
    /// <summary><para>The OEM minus key on any country/region keyboard (Windows 2000 or later).</para></summary>
    OemMinus = Oemcomma | LButton, // 0x000000BD
    /// <summary><para>The OEM period key on any country/region keyboard (Windows 2000 or later).</para></summary>
    OemPeriod = Oemcomma | RButton, // 0x000000BE
    /// <summary><para>The OEM question mark key on a US standard keyboard (Windows 2000 or later).</para></summary>
    OemQuestion = OemPeriod | LButton, // 0x000000BF
    /// <summary><para>The OEM tilde key on a US standard keyboard (Windows 2000 or later).</para></summary>
    Oemtilde = 192, // 0x000000C0
    /// <summary><para>The OEM open bracket key on a US standard keyboard (Windows 2000 or later).</para></summary>
    OemOpenBrackets = Oemtilde | Escape, // 0x000000DB
    /// <summary><para>The OEM pipe key on a US standard keyboard (Windows 2000 or later).</para></summary>
    OemPipe = Oemtilde | IMEConvert, // 0x000000DC
    /// <summary><para>The OEM close bracket key on a US standard keyboard (Windows 2000 or later).</para></summary>
    OemCloseBrackets = OemPipe | LButton, // 0x000000DD
    /// <summary><para>The OEM singled/double quote key on a US standard keyboard (Windows 2000 or later).</para></summary>
    OemQuotes = OemPipe | RButton, // 0x000000DE
    /// <summary><para>The OEM 8 key.</para></summary>
    Oem8 = OemQuotes | LButton, // 0x000000DF
    /// <summary><para>The OEM angle bracket or backslash key on the RT 102 key keyboard (Windows 2000 or later).</para></summary>
    OemBackslash = Oemtilde | Next, // 0x000000E2
    /// <summary><para>The PROCESS KEY key.</para></summary>
    ProcessKey = Oemtilde | Left, // 0x000000E5
    /// <summary><para>The ATTN key.</para></summary>
    Attn = OemBackslash | Capital, // 0x000000F6
    /// <summary><para>The CRSEL key.</para></summary>
    Crsel = Attn | LButton, // 0x000000F7
    /// <summary><para>The EXSEL key.</para></summary>
    Exsel = Oemtilde | D8, // 0x000000F8
    /// <summary><para>The ERASE EOF key.</para></summary>
    EraseEof = Exsel | LButton, // 0x000000F9
    /// <summary><para>The PLAY key.</para></summary>
    Play = Exsel | RButton, // 0x000000FA
    /// <summary><para>The ZOOM key.</para></summary>
    Zoom = Play | LButton, // 0x000000FB
    /// <summary><para>A constant reserved for future use.</para></summary>
    NoName = Exsel | MButton, // 0x000000FC
    /// <summary><para>The PA1 key.</para></summary>
    Pa1 = NoName | LButton, // 0x000000FD
    /// <summary><para>The CLEAR key.</para></summary>
    OemClear = NoName | RButton, // 0x000000FE
    /// <summary><para>The bitmask to extract a key code from a key value.</para></summary>
    KeyCode = 65535, // 0x0000FFFF
    /// <summary><para>The SHIFT modifier key.</para></summary>
    Shift = 65536, // 0x00010000
    /// <summary><para>The CTRL modifier key.</para></summary>
    Control = 131072, // 0x00020000
    /// <summary><para>The ALT modifier key.</para></summary>
    Alt = 262144, // 0x00040000
    /// <summary><para>The bitmask to extract modifiers from a key value.</para></summary>
    Modifiers = -65536, // 0xFFFF0000
    /// <summary><para>The IME accept key, replaces <see cref="F:System.Windows.Forms.Keys.IMEAceept" />.</para></summary>
    IMEAccept = IMEAceept, // 0x0000001E
    /// <summary><para>The OEM 1 key.</para></summary>
    Oem1 = OemSemicolon, // 0x000000BA
    /// <summary><para>The OEM 102 key.</para></summary>
    Oem102 = OemBackslash, // 0x000000E2
    /// <summary><para>The OEM 2 key.</para></summary>
    Oem2 = Oem1 | XButton1, // 0x000000BF
    /// <summary><para>The OEM 3 key.</para></summary>
    Oem3 = Oemtilde, // 0x000000C0
    /// <summary><para>The OEM 4 key.</para></summary>
    Oem4 = Oem3 | Escape, // 0x000000DB
    /// <summary><para>The OEM 5 key.</para></summary>
    Oem5 = Oem3 | IMEConvert, // 0x000000DC
    /// <summary><para>The OEM 6 key.</para></summary>
    Oem6 = Oem5 | LButton, // 0x000000DD
    /// <summary><para>The OEM 7 key.</para></summary>
    Oem7 = Oem5 | RButton, // 0x000000DE
    /// <summary><para>Used to pass Unicode characters as if they were keystrokes. The Packet key value is the low word of a 32-bit virtual-key value used for non-keyboard input methods.</para></summary>
    Packet = Oem3 | Right, // 0x000000E7
    /// <summary><para>The computer sleep key.</para></summary>
    Sleep = IMEAccept | A, // 0x0000005F
}

public static class KeysExtensions {
    public static WinFormsKeys ToWinForms(this Keys keys) => keys switch {
        Keys.None => WinFormsKeys.None,
        // Keys.Cancel => WinFormsKeys.Cancel,
        Keys.Backspace => WinFormsKeys.Back,
        Keys.Tab => WinFormsKeys.Tab,
        // Keys.LineFeed => WinFormsKeys.LineFeed,
        Keys.Clear => WinFormsKeys.Clear,
        Keys.Enter => WinFormsKeys.Return,
        Keys.Pause => WinFormsKeys.Pause,
        Keys.CapsLock => WinFormsKeys.CapsLock,
        // Keys.HangulMode => WinFormsKeys.HangulMode,
        // Keys.JunjaMode => WinFormsKeys.JunjaMode,
        // Keys.FinalMode => WinFormsKeys.FinalMode,
        // Keys.KanjiMode => WinFormsKeys.KanjiMode,
        Keys.Escape => WinFormsKeys.Escape,
        // Keys.ImeConvert => WinFormsKeys.IMEConvert,
        // Keys.ImeNonConvert => WinFormsKeys.IMENonconvert,
        // Keys.ImeAccept => WinFormsKeys.IMEAccept,
        // Keys.ImeModeChange => WinFormsKeys.IMEModeChange,
        Keys.Space => WinFormsKeys.Space,
        Keys.PageUp => WinFormsKeys.PageUp,
        Keys.PageDown => WinFormsKeys.PageDown,
        Keys.End => WinFormsKeys.End,
        Keys.Home => WinFormsKeys.Home,
        Keys.Left => WinFormsKeys.Left,
        Keys.Up => WinFormsKeys.Up,
        Keys.Right => WinFormsKeys.Right,
        Keys.Down => WinFormsKeys.Down,
        // Keys.Select => WinFormsKeys.Select,
        // Keys.Print => WinFormsKeys.Print,
        // Keys.Execute => WinFormsKeys.Execute,
        // Keys.Snapshot => WinFormsKeys.Snapshot,
        Keys.Insert => WinFormsKeys.Insert,
        Keys.Delete => WinFormsKeys.Delete,
        Keys.Help => WinFormsKeys.Help,
        Keys.D0 => WinFormsKeys.D0,
        Keys.D1 => WinFormsKeys.D1,
        Keys.D2 => WinFormsKeys.D2,
        Keys.D3 => WinFormsKeys.D3,
        Keys.D4 => WinFormsKeys.D4,
        Keys.D5 => WinFormsKeys.D5,
        Keys.D6 => WinFormsKeys.D6,
        Keys.D7 => WinFormsKeys.D7,
        Keys.D8 => WinFormsKeys.D8,
        Keys.D9 => WinFormsKeys.D9,
        Keys.A => WinFormsKeys.A,
        Keys.B => WinFormsKeys.B,
        Keys.C => WinFormsKeys.C,
        Keys.D => WinFormsKeys.D,
        Keys.E => WinFormsKeys.E,
        Keys.F => WinFormsKeys.F,
        Keys.G => WinFormsKeys.G,
        Keys.H => WinFormsKeys.H,
        Keys.I => WinFormsKeys.I,
        Keys.J => WinFormsKeys.J,
        Keys.K => WinFormsKeys.K,
        Keys.L => WinFormsKeys.L,
        Keys.M => WinFormsKeys.M,
        Keys.N => WinFormsKeys.N,
        Keys.O => WinFormsKeys.O,
        Keys.P => WinFormsKeys.P,
        Keys.Q => WinFormsKeys.Q,
        Keys.R => WinFormsKeys.R,
        Keys.S => WinFormsKeys.S,
        Keys.T => WinFormsKeys.T,
        Keys.U => WinFormsKeys.U,
        Keys.V => WinFormsKeys.V,
        Keys.W => WinFormsKeys.W,
        Keys.X => WinFormsKeys.X,
        Keys.Y => WinFormsKeys.Y,
        Keys.Z => WinFormsKeys.Z,
        Keys.LeftApplication => WinFormsKeys.LWin,
        Keys.RightApplication => WinFormsKeys.RWin,
        // Keys.Apps => WinFormsKeys.Apps,
        // Keys.Sleep => WinFormsKeys.Sleep,
        Keys.Keypad0 => WinFormsKeys.NumPad0,
        Keys.Keypad1 => WinFormsKeys.NumPad1,
        Keys.Keypad2 => WinFormsKeys.NumPad2,
        Keys.Keypad3 => WinFormsKeys.NumPad3,
        Keys.Keypad4 => WinFormsKeys.NumPad4,
        Keys.Keypad5 => WinFormsKeys.NumPad5,
        Keys.Keypad6 => WinFormsKeys.NumPad6,
        Keys.Keypad7 => WinFormsKeys.NumPad7,
        Keys.Keypad8 => WinFormsKeys.NumPad8,
        Keys.Keypad9 => WinFormsKeys.NumPad9,
        Keys.Multiply => WinFormsKeys.Multiply,
        Keys.Add => WinFormsKeys.Add,
        // Keys.Separator => WinFormsKeys.Separator,
        Keys.Subtract => WinFormsKeys.Subtract,
        Keys.Decimal => WinFormsKeys.Decimal,
        Keys.Divide => WinFormsKeys.Divide,
        Keys.F1 => WinFormsKeys.F1,
        Keys.F2 => WinFormsKeys.F2,
        Keys.F3 => WinFormsKeys.F3,
        Keys.F4 => WinFormsKeys.F4,
        Keys.F5 => WinFormsKeys.F5,
        Keys.F6 => WinFormsKeys.F6,
        Keys.F7 => WinFormsKeys.F7,
        Keys.F8 => WinFormsKeys.F8,
        Keys.F9 => WinFormsKeys.F9,
        Keys.F10 => WinFormsKeys.F10,
        Keys.F11 => WinFormsKeys.F11,
        Keys.F12 => WinFormsKeys.F12,
        Keys.F13 => WinFormsKeys.F13,
        Keys.F14 => WinFormsKeys.F14,
        Keys.F15 => WinFormsKeys.F15,
        Keys.F16 => WinFormsKeys.F16,
        Keys.F17 => WinFormsKeys.F17,
        Keys.F18 => WinFormsKeys.F18,
        Keys.F19 => WinFormsKeys.F19,
        Keys.F20 => WinFormsKeys.F20,
        Keys.F21 => WinFormsKeys.F21,
        Keys.F22 => WinFormsKeys.F22,
        Keys.F23 => WinFormsKeys.F23,
        Keys.F24 => WinFormsKeys.F24,
        Keys.NumberLock => WinFormsKeys.NumLock,
        Keys.ScrollLock => WinFormsKeys.Scroll,
        Keys.Shift => WinFormsKeys.Shift,
        Keys.LeftShift => WinFormsKeys.LShiftKey,
        Keys.RightShift => WinFormsKeys.RShiftKey,
        Keys.Control => WinFormsKeys.Control,
        Keys.LeftControl => WinFormsKeys.LControlKey,
        Keys.RightControl => WinFormsKeys.RControlKey,
        Keys.Alt => WinFormsKeys.Menu,
        Keys.LeftAlt => WinFormsKeys.LMenu,
        Keys.RightAlt => WinFormsKeys.RMenu,
        // Keys.BrowserBack => WinFormsKeys.BrowserBack,
        // Keys.BrowserForward => WinFormsKeys.BrowserForward,
        // Keys.BrowserRefresh => WinFormsKeys.BrowserRefresh,
        // Keys.BrowserStop => WinFormsKeys.BrowserStop,
        // Keys.BrowserSearch => WinFormsKeys.BrowserSearch,
        // Keys.BrowserFavorites => WinFormsKeys.BrowserFavorites,
        // Keys.BrowserHome => WinFormsKeys.BrowserHome,
        // Keys.VolumeMute => WinFormsKeys.VolumeMute,
        // Keys.VolumeDown => WinFormsKeys.VolumeDown,
        // Keys.VolumeUp => WinFormsKeys.VolumeUp,
        // Keys.MediaNextTrack => WinFormsKeys.MediaNextTrack,
        // Keys.MediaPreviousTrack => WinFormsKeys.MediaPreviousTrack,
        // Keys.MediaStop => WinFormsKeys.MediaStop,
        // Keys.MediaPlayPause => WinFormsKeys.MediaPlayPause,
        // Keys.LaunchMail => WinFormsKeys.LaunchMail,
        // Keys.SelectMedia => WinFormsKeys.SelectMedia,
        // Keys.LaunchApplication1 => WinFormsKeys.LaunchApplication1,
        // Keys.LaunchApplication2 => WinFormsKeys.LaunchApplication2,
        Keys.Semicolon => WinFormsKeys.OemSemicolon,
        Keys.Equal => WinFormsKeys.Oemplus,
        Keys.Comma => WinFormsKeys.Oemcomma,
        Keys.Minus => WinFormsKeys.OemMinus,
        Keys.Period => WinFormsKeys.OemPeriod,
        Keys.Slash => WinFormsKeys.OemQuestion,
        // Keys.Question => WinFormsKeys.OemQuestion,
        // Keys.Tilde => WinFormsKeys.Oemtilde,
        // Keys.AbntC1 => Keys.AbntC1,
        // Keys.AbntC2 => Keys.AbntC2,
        Keys.LeftBracket => WinFormsKeys.OemOpenBrackets,
        // Keys.OemPipe => WinFormsKeys.OemPipe,
        Keys.RightBracket => WinFormsKeys.OemCloseBrackets,
        Keys.Quote => WinFormsKeys.OemQuotes,
        // Keys.Oem8 => WinFormsKeys.Oem8,
        Keys.Backslash => WinFormsKeys.OemBackslash,
        // Keys.ImeProcessed => Keys.ImeProcessed,
        // Keys.System => Keys.System,
        // Keys.OemAttn => Keys.OemAttn,
        // Keys.OemFinish => Keys.OemFinish,
        // Keys.DbeHiragana => Keys.DbeHiragana,
        // Keys.DbeSbcsChar => Keys.DbeSbcsChar,
        // Keys.DbeDbcsChar => Keys.DbeDbcsChar,
        // Keys.OemBackTab => Keys.OemBackTab,
        // Keys.DbeNoRoman => Keys.DbeNoRoman,
        // Keys.CrSel => WinFormsKeys.Crsel,
        // Keys.ExSel => WinFormsKeys.Exsel,
        // Keys.EraseEof => WinFormsKeys.EraseEof,
        // Keys.Play => WinFormsKeys.Play,
        // Keys.DbeNoCodeInput => Keys.DbeNoCodeInput,
        // Keys.NoName => WinFormsKeys.NoName,
        // Keys.DbeEnterDialogConversionMode => Keys.DbeEnterDialogConversionMode,
        // Keys.OemClear => WinFormsKeys.OemClear,
        // Keys.DeadCharProcessed => Keys.DeadCharProcessed,
        // Keys.FnLeftArrow => Keys.FnLeftArrow,
        // Keys.FnRightArrow => Keys.FnRightArrow,
        // Keys.FnUpArrow => Keys.FnUpArrow,
        // Keys.FnDownArrow => Keys.FnDownArrow,
        Keys.Grave => WinFormsKeys.Oemtilde,
        Keys.PrintScreen => WinFormsKeys.PrintScreen,
        // Keys.ContextMenu => ,
        Keys.Application => WinFormsKeys.LWin,
        _ => throw new ArgumentOutOfRangeException(nameof(keys), keys, null)
    };
}
