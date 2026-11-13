"""Tiny newline-delimited JSON-RPC host used by the MCP client tests."""
import json
import sys


def main() -> None:
    for line in sys.stdin:
        try:
            message = json.loads(line)
        except json.JSONDecodeError:
            continue
        if "id" not in message:
            continue
        method = message.get("method")
        if method == "initialize":
            result = {
                "protocolVersion": "2025-06-18",
                "capabilities": {},
                "serverInfo": {"name": "fake", "version": "0"},
            }
        elif method == "tools/call":
            params = message.get("params") or {}
            name = params.get("name")
            arguments = params.get("arguments") or {}
            if name == "echo":
                result = {"content": [{"type": "text",
                                       "text": json.dumps(arguments)}]}
            elif name == "fail":
                result = {"isError": True,
                          "content": [{"type": "text", "text": "boom"}]}
            else:
                result = {"isError": True,
                          "content": [{"type": "text",
                                       "text": "unknown tool " + str(name)}]}
        else:
            result = {"content": [{"type": "text",
                                   "text": json.dumps({"method": method})}]}
        print(json.dumps({"jsonrpc": "2.0", "id": message["id"],
                          "result": result}), flush=True)


if __name__ == "__main__":
    main()
