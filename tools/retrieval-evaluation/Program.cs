using EnterpriseDocumentAssistant.Api.Documents;
using EnterpriseDocumentAssistant.RetrievalEvaluation;

return await RetrievalEvaluationCommand.RunAsync(
    args,
    new DeterministicEmbeddingGenerator(),
    new InMemorySemanticIndexStore(),
    CancellationToken.None);
