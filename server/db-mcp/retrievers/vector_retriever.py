# retrievers/vector_retriever.py
# Dense 벡터 검색 전용 retriever (1차 배포용)

from langchain_core.documents import Document

RETRIEVAL_K = 20


class VectorRetriever:
    def __init__(self, vector_dbs: dict):
        self.vector_dbs = vector_dbs

    def search(self, query: str) -> list[Document]:
        all_docs = []
        for vdb in self.vector_dbs.values():
            results = vdb.similarity_search_with_relevance_scores(query, k=RETRIEVAL_K)
            valid   = [doc for doc, score in results if score > 0.2]
            if not valid and results:
                valid = [results[0][0]]
            all_docs.extend(valid)

        seen, deduped = set(), []
        for doc in all_docs:
            if doc.page_content not in seen:
                seen.add(doc.page_content)
                deduped.append(doc)
        return deduped
