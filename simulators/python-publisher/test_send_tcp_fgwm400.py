import unittest
from types import SimpleNamespace

from send_tcp_fgwm400 import text_line


class PacketFormatTests(unittest.TestCase):
    def test_prefix_extended_packet_has_eleven_fields_and_no_newline(self):
        options = SimpleNamespace(text_layout="extended", phone=True, phone_number="7397097255", prefix=True, no_newline=True)
        packet = text_line("100000005", -16.6001, -.2656, 77.0541, 31.0773, 5, options)
        self.assertNotIn(b"\n", packet)
        fields = packet.decode().split(",")
        self.assertEqual(len(fields), 11)
        self.assertEqual(fields[:5], ["BW-NB-WM400", "100000005", "-016.6001", "-000.2656", "+077.0541"])
        self.assertEqual(fields[-3:], ["+031.0773", "5", "7397097255"])

    def test_compact_packet_keeps_temperature_in_column_four_and_battery_in_five(self):
        options = SimpleNamespace(text_layout="compact", phone=False, prefix=False)
        packet = text_line("100000005", -16.5923, -0.2516, 88.0, 31.2983, 5, options)
        self.assertEqual(packet, b"100000005,-016.5923,-000.2516,+031.2983,5\n")

    def test_extended_packet_preserves_existing_nine_field_format(self):
        options = SimpleNamespace(text_layout="extended", phone=False, prefix=False)
        fields = text_line("100000005", 22.2149, 0.5842, 88.8487, 28.87, 7, options).decode().strip().split(",")
        self.assertEqual(len(fields), 9)
        self.assertEqual(fields[:4], ["100000005", "+022.2149", "+000.5842", "+088.8487"])
        self.assertEqual(fields[-2:], ["+028.8700", "7"])


if __name__ == "__main__":
    unittest.main()
