using JapaneseLearningPlatform.Data.ViewModels;

namespace JapaneseLearningPlatform.Data.Services
{
    public interface IAIExplanationService
    {
        Task<string> GetExplanationAsync(string question, string userAnswer, string correctAnswer);

        // Batch request: returns a mapping from QuestionId to explanation text
        Task<Dictionary<int, string>> GetExplanationsAsync(IEnumerable<AIExplanationRequest> requests);
    }
}
