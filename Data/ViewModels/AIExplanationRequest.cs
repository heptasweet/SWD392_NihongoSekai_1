namespace JapaneseLearningPlatform.Data.ViewModels
{
    public class AIExplanationRequest
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; }
        public string UserAnswer { get; set; }
        public string CorrectAnswer { get; set; }
    }
}
