namespace JapaneseLearningPlatform.Data.ViewModels
{
    public class QuizOptionVM
    {
        public int OptionId { get; set; }
        public string OptionText { get; set; }
        public bool IsCorrect { get; set; } // Chỉ dùng để kiểm tra sau
        public bool IsSelected { get; set; } // Chỉ dùng cho UI hiển thị lựa chọn của user
    }
}
