"""Local protocol fixture, not a model. No network forwarding, keys, or request logging."""
import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_POST(self):
        if self.path != "/v1/chat/completions":
            self.send_error(404)
            return
        if self.server.mode == "auth-error":
            self.send_error(401)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if size > 1_000_000:
                self.send_error(413)
                return
            request = json.loads(self.rfile.read(size))
            result = "这是本机模拟接口的测试结果。\n\n请明确任务目标、输入条件与期望的输出格式；保留原始意图，不编造事实。"
            if not request.get("stream"):
                payload = json.dumps({"choices": [{"message": {"content": result}, "finish_reason": "stop"}]}, ensure_ascii=False).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
                return
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream; charset=utf-8")
            self.send_header("Cache-Control", "no-cache")
            self.end_headers()
            for index in range(0, len(result), 3):
                self.write_event({"choices": [{"delta": {"content": result[index:index+3]}, "finish_reason": None}]})
                time.sleep(self.server.delay)
            if self.server.mode != "truncated":
                self.write_event({"choices": [{"delta": {}, "finish_reason": "stop"}]})
                self.wfile.write(b"data: [DONE]\n\n")
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            pass
        except (ValueError, KeyError):
            self.send_error(400)

    def write_event(self, event):
        self.wfile.write(("data: " + json.dumps(event, ensure_ascii=False) + "\n\n").encode("utf-8"))
        self.wfile.flush()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=18765)
    parser.add_argument("--delay", type=float, default=0.03)
    parser.add_argument("--mode", choices=["success", "truncated", "auth-error"], default="success")
    options = parser.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", options.port), Handler)
    server.mode, server.delay = options.mode, options.delay
    print(f"Local fixture: http://127.0.0.1:{options.port}/v1", flush=True)
    server.serve_forever()
