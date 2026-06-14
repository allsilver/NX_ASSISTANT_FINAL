import unittest

from knox_mail_automation.parser import CommandParseError, parse_chat_command


class ParseChatCommandTest(unittest.TestCase):
    def test_parses_basic_quoted_command(self) -> None:
        message = parse_chat_command('수신자 서다은에게 "hi"라고 적고 발송해줘')

        self.assertEqual(message.recipient, "서다은")
        self.assertEqual(message.body, "hi")

    def test_parses_without_recipient_prefix(self) -> None:
        message = parse_chat_command("서다은에게 '안녕하세요'라고 보내줘")

        self.assertEqual(message.recipient, "서다은")
        self.assertEqual(message.body, "안녕하세요")

    def test_parses_subject(self) -> None:
        message = parse_chat_command('수신자 서다은에게 "hi"라고 적고 제목: 인사 발송해줘')

        self.assertEqual(message.subject, "인사")

    def test_rejects_unclear_command(self) -> None:
        with self.assertRaises(CommandParseError):
            parse_chat_command("메일 보내줘")


if __name__ == "__main__":
    unittest.main()
