# llm/__init__.py
# LLM 팩토리 — model_name prefix로 분기
#   "gauss:o4-think"  → GaussLLM
#   "gauss:o4-instruct" → GaussLLM
#   그 외             → OllamaLLM (로컬 테스트용)


def get_llm(model_name: str, num_ctx: int = 4096, exp_config: dict = None):
    """
    model_name prefix에 따라 적절한 LLM 객체 반환.
    반환값은 모두 LangChain BaseLLM을 상속하므로
    `prompt | llm | StrOutputParser()` 파이프라인에 바로 사용 가능.
    """
    if exp_config is None:
        exp_config = {}

    if model_name.startswith("gauss:"):
        from .gauss_llm import GaussLLM

        model_alias = model_name.split(":", 1)[1]
        gauss_cfg   = exp_config.get("gauss", {})
        models      = gauss_cfg.get("models", {})

        if model_alias not in models:
            raise ValueError(
                f"settings.json의 gauss.models에 '{model_alias}' 설정이 없습니다.\n"
                f"등록된 모델: {list(models.keys())}"
            )

        model_cfg = models[model_alias]
        return GaussLLM(
            model_alias = model_name,
            api_url     = model_cfg.get("api_url",    ""),
            access_key  = gauss_cfg.get("access_key", ""),
            secret_key  = gauss_cfg.get("secret_key", ""),
            model_name  = model_cfg.get("model_name", ""),
        )

    # 기본: Ollama (로컬 테스트용)
    from langchain_ollama import OllamaLLM
    return OllamaLLM(model=model_name, temperature=0.1, num_ctx=num_ctx)
