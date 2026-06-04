# llm/gauss_llm.py
# Gauss API를 LangChain BaseLLM으로 래핑.
# 스트리밍 지원 — _stream()은 SSE(text/event-stream) 청크 단위 yield.

import os
import base64
from typing import Any, Iterator, List, Optional

import requests
import urllib3

from langchain_core.language_models.llms import BaseLLM
from langchain_core.outputs import Generation, LLMResult

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

os.environ["no_proxy"] = (
    os.environ.get("no_proxy", "") + ",*.sr-cloud.com,inference-webtrial-api.shuttle.sr-cloud.com"
).lstrip(",")
os.environ["NO_PROXY"] = os.environ["no_proxy"]

PROXIES = {"http": None, "https": None}
TIMEOUT = 180


class GaussLLM(BaseLLM):
    """
    Gauss API LangChain 래퍼.

    사용법:
        llm = GaussLLM(
            model_alias = "gauss:o4-think",
            api_url     = "https://...",
            access_key  = "...",
            secret_key  = "...",
            model_name  = "GaussO4_Think_250902-fp8",
        )
        chain = prompt | llm | StrOutputParser()
    """

    model_alias: str = "gauss:o4-think"
    api_url:     str = ""
    access_key:  str = ""
    secret_key:  str = ""
    model_name:  str = ""
    temperature: float = 0.1
    top_p:       float = 0.96
    repetition_penalty: float = 1.03

    @property
    def _llm_type(self) -> str:
        return "gauss"

    def _build_headers(self) -> dict:
        creds   = f"{self.access_key}:{self.secret_key}"
        encoded = base64.b64encode(creds.encode()).decode()
        return {
            "accept":        "*/*",
            "Authorization": f"Basic {encoded}",
            "Content-Type":  "application/json",
        }

    def _build_payload(self, prompt: str, stream: bool = False) -> dict:
        return {
            "messages":           [{"role": "user", "content": prompt}],
            "model":              self.model_name,
            "stream":             stream,
            "temperature":        self.temperature,
            "top_p":              self.top_p,
            "repetition_penalty": self.repetition_penalty,
        }

    def _call_api(self, prompt: str) -> str:
        response = requests.post(
            self.api_url,
            headers = self._build_headers(),
            json    = self._build_payload(prompt, stream=False),
            proxies = PROXIES,
            verify  = False,
            timeout = TIMEOUT,
        )
        if response.status_code == 200:
            return response.json()["choices"][0]["message"]["content"]
        raise RuntimeError(
            f"[Gauss API ERROR] {response.status_code}\n{response.text[:300]}"
        )

    def _generate(
        self,
        prompts:     List[str],
        stop:        Optional[List[str]] = None,
        run_manager: Any = None,
        **kwargs,
    ) -> LLMResult:
        generations = []
        for prompt in prompts:
            text = self._call_api(prompt)
            generations.append([Generation(text=text)])
        return LLMResult(generations=generations)

    def _stream(
        self,
        prompt:      str,
        stop:        Optional[List[str]] = None,
        run_manager: Any = None,
        **kwargs,
    ) -> Iterator:
        """SSE 스트리밍 — OpenAI 호환 포맷"""
        import json as _json
        from langchain_core.outputs import GenerationChunk

        response = requests.post(
            self.api_url,
            headers = self._build_headers(),
            json    = self._build_payload(prompt, stream=True),
            proxies = PROXIES,
            verify  = False,
            timeout = TIMEOUT,
            stream  = True,
        )
        if response.status_code != 200:
            raise RuntimeError(
                f"[Gauss API ERROR] {response.status_code}\n{response.text[:300]}"
            )

        for line in response.iter_lines():
            if not line:
                continue
            decoded = line.decode("utf-8")
            if not decoded.startswith("data:"):
                continue
            data_str = decoded[len("data:"):].strip()
            if data_str == "[DONE]":
                break
            try:
                data  = _json.loads(data_str)
                delta = data["choices"][0].get("delta", {})
                text  = delta.get("content", "")
                if text:
                    yield GenerationChunk(text=text)
            except Exception:
                continue
