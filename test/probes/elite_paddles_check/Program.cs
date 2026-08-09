// Xbox Elite paddle wire check (issue #49).
//
// The Elite descriptors grew from 10 to 14 declared buttons (padding
// 6b -> 2b, 16-bit envelope unchanged) so the four rear paddles have a
// HID-visible carrier at descriptor buttons 11-14. Real hardware never
// exposes paddles in any HID view: over USB the controller is a vendor
// FF-class interface (ControllersInfo Model 1797 dump) and the paddles
// ride GIP unmapped-state messages (SDL_hidapi_xboxone.c,
// HandleUnmappedStatePacket, bits 0x01/0x02/0x04/0x08 at byte 14); over
// BLE they ride the Consumer Assign Selection element
// (SDL_hidapi_xboxone.c HandleElement, xpadneo #427). A declared-button
// carrier is therefore the only representation a HID consumer can read,
// the same call issue #50 made for the Series Share button.
//
// Paddle ORDER is transcribed from SDL's Elite mapping, not invented:
// SDL_gamepad.c maps paddle1:b11, paddle3:b12, paddle2:b13, paddle4:b14
// for SDL_IsJoystickXboxOneElite, and SDL_gamepad.h names RIGHT_PADDLE1
// "Xbox Elite paddle P1" / LEFT_PADDLE1 "P3". So the wire order at
// b11..b14 is P1, P2, P3, P4 - matching the GIP bitfield order - and
// HMButton's side-named bits land as RightPaddle=P1, RightPaddle2=P2,
// LeftPaddle=P3, LeftPaddle2=P4.
//
// Report layout (no report ID): sticks 0-7, Z 8-9, Rz 10-11, buttons
// at bytes 12-13 (14 bits + 2 pad), hat byte 14.
//
// Exit 0 PASS / 1 FAIL. No elevation and no device required.

using System;

using HIDMaestro;
using HIDMaestro.Internal;

internal static class Program
{
    static int s_total, s_failures;

    static void Check(string name, bool cond, string detail = "")
    {
        s_total++;
        if (!cond) s_failures++;
        Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? "  " + detail : "")}");
    }

    static byte[] Build(HidReportBuilder b, HMButton buttons)
    {
        var report = new byte[b.InputReportByteSize];
        b.BuildReportInto(report, axes: null, buttonMask: (uint)buttons);
        return report;
    }

    static int Main()
    {
        Console.WriteLine("=== Xbox Elite paddles: declared-button carrier (issue #49) ===");

        using var ctx = new HMContext();
        ctx.LoadDefaultProfiles();

        HMProfile P(string id) => ctx.GetProfile(id) ?? throw new Exception($"missing profile {id}");
        HidReportBuilder B(string id) => P(id).Inner.GetOrBuildReportBuilder();

        // All four Elite profiles carry the same widened GIP view and map.
        foreach (var id in new[] { "xbox-elite-v2", "xbox-elite-v2-bt", "xbox-elite-v2-bt-v2", "xbox-elite-v1" })
        {
            Console.WriteLine($"\n-- {id} --");
            var b = B(id);
            Check("declares 14 buttons (10 GIP + 4 paddles)",
                  b.Buttons.Count == 14, $"got {b.Buttons.Count}");

            // Byte 12 = buttons 1-8, byte 13 = buttons 9-14 + 2 pad bits.
            var r = Build(b, HMButton.A);
            Check("A still lands at button 1 (byte12 bit0)", r[12] == 0x01 && r[13] == 0x00,
                  $"b12=0x{r[12]:X2} b13=0x{r[13]:X2}");
            r = Build(b, HMButton.RightStick);
            Check("R3 still lands at button 10 (byte13 bit1)", r[13] == 0x02, $"b13=0x{r[13]:X2}");

            // The paddles, in SDL's Elite order: b11..b14 = P1, P2, P3, P4.
            r = Build(b, HMButton.RightPaddle);
            Check("RightPaddle (P1) sets button 11 (byte13 0x04)", r[13] == 0x04, $"b13=0x{r[13]:X2}");
            r = Build(b, HMButton.RightPaddle2);
            Check("RightPaddle2 (P2) sets button 12 (byte13 0x08)", r[13] == 0x08, $"b13=0x{r[13]:X2}");
            r = Build(b, HMButton.LeftPaddle);
            Check("LeftPaddle (P3) sets button 13 (byte13 0x10)", r[13] == 0x10, $"b13=0x{r[13]:X2}");
            r = Build(b, HMButton.LeftPaddle2);
            Check("LeftPaddle2 (P4) sets button 14 (byte13 0x20)", r[13] == 0x20, $"b13=0x{r[13]:X2}");
            r = Build(b, HMButton.RightPaddle | HMButton.RightPaddle2 | HMButton.LeftPaddle | HMButton.LeftPaddle2);
            Check("all four paddles read 0x3C, pad bits stay clear", r[13] == 0x3C, $"b13=0x{r[13]:X2}");

            // The -1 sentinels: buttons this hardware does not carry must
            // stay dead rather than falling through to the new indices
            // (the pre-#48 aliasing failure mode, re-checked here because
            // widening the array is exactly what re-opens it).
            r = Build(b, HMButton.Touchpad | HMButton.Share | HMButton.Misc1);
            Check("Touchpad/Share/Misc1 stay dead (sentinel -1, no aliasing onto paddles)",
                  r[12] == 0x00 && r[13] == 0x00, $"b12=0x{r[12]:X2} b13=0x{r[13]:X2}");

            // Guide routes through the System Main Menu field, not the
            // button array; with the map's -1 it must not double-land.
            r = Build(b, HMButton.Guide);
            Check("Guide stays out of the button array (System Main Menu path)",
                  r[12] == 0x00 && r[13] == 0x00 && r[15] == 0x01,
                  $"b12=0x{r[12]:X2} b13=0x{r[13]:X2} b15=0x{r[15]:X2}");
        }

        // The Adaptive and One-family pads share the base GIP descriptor
        // and must be untouched: 10 buttons, paddle bits dead.
        Console.WriteLine("\n-- xbox-one-s (control: base GIP view unchanged) --");
        var one = B("xbox-one-s");
        Check("xbox-one-s still declares 10 buttons", one.Buttons.Count == 10, $"got {one.Buttons.Count}");
        var ro = Build(one, HMButton.RightPaddle | HMButton.LeftPaddle | HMButton.RightPaddle2 | HMButton.LeftPaddle2);
        Check("paddles do nothing on xbox-one-s", ro[12] == 0x00 && ro[13] == 0x00,
              $"b12=0x{ro[12]:X2} b13=0x{ro[13]:X2}");

        Console.WriteLine($"\n=== {s_total - s_failures}/{s_total} checks passed ===");
        return s_failures == 0 ? 0 : 1;
    }
}
