[ ] Create an OpenAI compatible REST API endpoint
[ ] Build into a docker container
[ ] Provide a docker compose file
[ ] Read a set of destination URLs, API keys, priorities and model names from a JSON file
[ ] Implement capability-based model routing (chat, embeddings, transcriptions, etc.)
[ ] Allow providers to specify supported capabilities with cost/complexity metrics
[ ] Support * wildcard for providers that support all capabilities
[ ] Return 503 Service Unavailable when no provider is available for requested capability
[ ] Forward requests to the destination URL and return the response to the caller
[ ] If there is an error, switch to the next provider in order of priority that supports the capability
[ ] Semantic logging with Serilog
[ ] Write a daily jsonL log with the token counts that are included with the final chat response