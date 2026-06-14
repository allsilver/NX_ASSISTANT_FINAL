import unittest

from knox_mail_automation.server import handle


class ServerTest(unittest.IsolatedAsyncioTestCase):
    async def test_initialize(self) -> None:
        response = await handle({"id": 0, "method": "initialize", "params": {}})

        self.assertEqual(response["id"], 0)
        self.assertEqual(response["result"]["serverInfo"]["name"], "knox-mail-automation")

    async def test_tools_call_returns_mcp_shaped_content(self) -> None:
        response = await handle(
            {
                "id": 2,
                "method": "tools/call",
                "params": {
                    "name": "send_knox_mail_from_chat",
                    "arguments": {
                        "text": '수신자 서다은에게 "hi"라고 적고 발송해줘',
                        "dryRun": True,
                    },
                },
            }
        )

        result = response["result"]
        self.assertEqual(result["structuredContent"]["message"]["recipient"], "서다은")
        self.assertEqual(result["structuredContent"]["message"]["body"], "hi")
        self.assertEqual(result["content"][0]["type"], "text")


if __name__ == "__main__":
    unittest.main()
