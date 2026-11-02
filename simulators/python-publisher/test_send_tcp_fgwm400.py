import unittest
from types import SimpleNamespace

from send_tcp_fgwm400 import text_line


class PacketFormatTests(unittest.TestCase):
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
