using JapaneseLearningPlatform.Data;
using JapaneseLearningPlatform.Data.Enums;
using JapaneseLearningPlatform.Data.Services;
using JapaneseLearningPlatform.Data.ViewModels;
using JapaneseLearningPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace JapaneseLearningPlatform.Controllers
{
    [Authorize(Roles = "Learner,Admin")]
    public class QuizzesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IQuizQuestionsService _questionsService;
        private readonly IAIExplanationService _aiService;
        private readonly ILogger<QuizzesController> _logger;
        // DTO for parsing QuestionsJson payload from the Start view
        private record SelectionDto(int questionId, List<int> selected);
        public QuizzesController(AppDbContext context, IQuizQuestionsService questionsService, IAIExplanationService aiService, ILogger<QuizzesController> logger)
        {
            _context = context;
            _questionsService = questionsService;
            _aiService = aiService;
            _logger = logger;
        }

         public async Task<IActionResult> Details(int id)
    {
        var quiz = await _context.Quizzes
            .Include(q => q.Questions)
                .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (quiz == null) return NotFound();

        return View(quiz);
    }

        // GET: /Quizzes/Start/5
        [HttpGet]
        public async Task<IActionResult> Start(int id)
        {
            var quiz = await _context.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (quiz == null)
                return View("NotFound");

            var vm = new TakeQuizVM
            {
                QuizId = quiz.Id,
                QuizTitle = quiz.Title,
                Questions = quiz.Questions.Select(q => new QuizQuestionVM
                {
                    QuestionId = q.Id,
                    QuestionText = q.QuestionText,
                    QuestionType = q.QuestionType, // <-- THIS
                    Options = q.Options.Select(o => new QuizOptionVM
                    {
                        OptionId = o.Id,
                        OptionText = o.OptionText,
                        IsCorrect = o.IsCorrect
                    }).ToList()
                }).ToList()
            };

            return View(vm); // Trả về View để chọn đáp án
        }

        // POST: /Quizzes/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(TakeQuizVM model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            // If the view emitted a JSON payload of selections (QuestionsJson), prefer that mapping
            var qjson = Request.Form["QuestionsJson"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(qjson))
            {
                try
                {
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var selections = JsonSerializer.Deserialize<List<SelectionDto>>(qjson, opts);
                    if (selections != null)
                    {
                        foreach (var sel in selections)
                        {
                            // find matching question in model by QuestionId
                            var mq = model.Questions.FirstOrDefault(x => x.QuestionId == sel.questionId);
                            if (mq == null) continue;

                            if (mq.QuestionType == QuestionType.SingleChoice)
                            {
                                mq.SelectedOptionId = sel.selected?.FirstOrDefault();
                            }
                            else if (mq.QuestionType == QuestionType.MultipleChoice)
                            {
                                mq.SelectedOptionIds = sel.selected ?? new List<int>();
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // ignore parse errors and fall back to legacy form parsing
                }
            }

            // Ensure selections are populated from the form (robust against model-binding issues)
            for (var qi = 0; qi < model.Questions.Count; qi++)
            {
                var q = model.Questions[qi];
                if (q == null) continue;

                if (q.QuestionType == QuestionType.SingleChoice)
                {
                    // don't overwrite if already set by QuestionsJson
                    if (q.SelectedOptionId == null)
                    {
                        var val = Request.Form[$"Questions[{qi}].SelectedOptionId"].FirstOrDefault();
                        if (int.TryParse(val, out var id)) q.SelectedOptionId = id;
                    }
                }
                else if (q.QuestionType == QuestionType.MultipleChoice)
                {
                    // don't overwrite if already set by QuestionsJson
                    if (q.SelectedOptionIds == null || !q.SelectedOptionIds.Any())
                    {
                        var values = Request.Form[$"Questions[{qi}].SelectedOptionIds"];
                        var selected = new List<int>();
                        foreach (var v in values)
                        {
                            if (string.IsNullOrWhiteSpace(v)) continue;
                            // handle both multiple form entries and comma-separated lists
                            var parts = v.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var p in parts)
                            {
                                if (int.TryParse(p.Trim(), out var id)) selected.Add(id);
                            }
                        }
                        q.SelectedOptionIds = selected;
                    }
                }
            }

            // (debug logging removed)

            int totalQuestions = model.Questions.Count;
            int correctAnswers = 0;

            var quiz = await _context.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == model.QuizId);

            if (quiz == null) return NotFound();

            var result = new QuizResult
            {
                QuizId = model.QuizId,
                UserId = userId,
                SubmittedAt = DateTime.UtcNow,
                TotalQuestions = totalQuestions,
                Details = new List<QuizResultDetail>()
            };

            var aiRequests = new List<AIExplanationRequest>();

            for (var qi = 0; qi < model.Questions.Count; qi++)
            {
                var question = model.Questions[qi];
                bool isCorrect = false;

                // Lấy câu hỏi từ DB để có danh sách option chính xác
                var dbQuestion = quiz.Questions.FirstOrDefault(q => q.Id == question.QuestionId);
                if (dbQuestion == null) continue;

                // Lấy danh sách đáp án đúng
                var correctOptions = dbQuestion.Options.Where(o => o.IsCorrect).ToList();
                string correctAnswerText = string.Join(", ", correctOptions.Select(o => o.OptionText));

                if (question.QuestionType == QuestionType.SingleChoice)
                {
                    // fallback: try read from form if model binding didn't bind radio value
                    if (question.SelectedOptionId == null)
                    {
                        var frmVal = Request.Form[$"Questions[{qi}].SelectedOptionId"].FirstOrDefault();
                        if (int.TryParse(frmVal, out var val)) question.SelectedOptionId = val;
                    }

                    if (question.SelectedOptionId != null)
                    {
                        var selectedOption = dbQuestion.Options.FirstOrDefault(o => o.Id == question.SelectedOptionId);
                        isCorrect = selectedOption?.IsCorrect ?? false;
                        if (isCorrect) correctAnswers++;

                        result.Details.Add(new QuizResultDetail
                        {
                            QuestionId = question.QuestionId,
                            SelectedOptionId = selectedOption?.Id,
                            IsCorrect = isCorrect
                        });
                    }
                }
                else if (question.QuestionType == QuestionType.MultipleChoice)
                {
                    // try to use model bound list; if empty, fallback to reading Request.Form values
                    var selectedIds = (question.SelectedOptionIds != null && question.SelectedOptionIds.Any())
                        ? question.SelectedOptionIds
                        : new List<int>();

                    if (!selectedIds.Any())
                    {
                        var values = Request.Form[$"Questions[{qi}].SelectedOptionIds"];
                        foreach (var v in values)
                        {
                            if (string.IsNullOrWhiteSpace(v)) continue;
                            var parts = v.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var p in parts)
                            {
                                if (int.TryParse(p.Trim(), out var id)) selectedIds.Add(id);
                            }
                        }
                        question.SelectedOptionIds = selectedIds;
                    }

                    // normalize selections and correct ids (dedupe) before comparison
                    selectedIds = selectedIds.Distinct().ToList();
                    var correctOptionIds = correctOptions.Select(o => o.Id).Distinct().ToList();

                    // (debug logs removed)

                    isCorrect = selectedIds.Count == correctOptionIds.Count &&
                                !selectedIds.Except(correctOptionIds).Any();

                    if (isCorrect) correctAnswers++;

                    result.Details.Add(new QuizResultDetail
                    {
                        QuestionId = question.QuestionId,
                        SelectedOptionId = null,
                        IsCorrect = isCorrect
                    });
                }

                // If answer is wrong, queue request for batch AI explanation
                if (!isCorrect)
                {
                    var userAnswerText = string.Join(", ", question.Options
                        .Where(o => question.SelectedOptionIds?.Contains(o.OptionId) == true || o.OptionId == question.SelectedOptionId)
                        .Select(o => o.OptionText));

                    aiRequests.Add(new AIExplanationRequest
                    {
                        QuestionId = question.QuestionId,
                        QuestionText = question.QuestionText,
                        UserAnswer = userAnswerText,
                        CorrectAnswer = correctAnswerText
                    });
                }

                // Gán lại IsCorrect cho UI
                    foreach (var opt in question.Options)
                    {
                        opt.IsCorrect = correctOptions.Any(co => co.Id == opt.OptionId);
                        // mark selected for UI
                        if (question.QuestionType == QuestionType.SingleChoice)
                            opt.IsSelected = opt.OptionId == question.SelectedOptionId;
                        else
                            opt.IsSelected = question.SelectedOptionIds?.Contains(opt.OptionId) == true;
                    }
            }

            result.Score = correctAnswers;
            _context.QuizResults.Add(result);
            await _context.SaveChangesAsync();

            // Call AI once for all wrong questions to reduce API calls and latency
            if (aiRequests.Any())
            {
                Dictionary<int, string> aiResponses;
                try
                {
                    aiResponses = await _aiService.GetExplanationsAsync(aiRequests);
                }
                catch (Exception ex)
                {
                    // Fill errors for each request
                    aiResponses = aiRequests.ToDictionary(r => r.QuestionId, r => $"[AI Error]: {ex.Message}");
                }

                // Map explanations back to the model questions
                foreach (var q in model.Questions)
                {
                    if (aiResponses.TryGetValue(q.QuestionId, out var expl))
                    {
                        q.AIExplanation = expl;
                    }
                }
            }

            model.CourseId = quiz.CourseId; // add courseId to model for result view

            ViewBag.TotalQuestions = totalQuestions;
            ViewBag.CorrectAnswers = correctAnswers;
            ViewBag.ScorePercent = (int)((double)correctAnswers / totalQuestions * 100);

            if (ViewBag.ScorePercent >= 80)
            {
                // Lấy ContentItemId của quiz
                var quizContentItem = await _context.CourseContentItems
                    .FirstOrDefaultAsync(ci => ci.QuizId == quiz.Id && ci.Section.CourseId == quiz.CourseId);

                if (quizContentItem != null)
                {
                    await MarkContentAsCompleted(quiz.CourseId, quizContentItem.Id, userId);
                }
            }

            return View("Result", model);
        }

        private async Task MarkContentAsCompleted(int courseId, int contentItemId, string userId)
        {
            var existing = await _context.CourseContentProgresses
                .FirstOrDefaultAsync(p => p.UserId == userId &&
                                          p.CourseId == courseId &&
                                          p.ContentItemId == contentItemId);

            if (existing == null)
            {
                _context.CourseContentProgresses.Add(new CourseContentProgress
                {
                    UserId = userId,
                    CourseId = courseId,
                    ContentItemId = contentItemId,
                    IsCompleted = true,
                    CompletedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.IsCompleted = true;
                existing.CompletedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
        }
        public async Task<IActionResult> Manage(int id)
        {
            var quiz = await _context.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (quiz == null) return NotFound();

            ViewData["QuizTitle"] = quiz.Title;
            ViewData["CourseId"] = quiz.CourseId;

            return View(quiz);
        }

        [HttpGet]
        [AllowAnonymous] // Cho phép mọi user xem trước
        public async Task<IActionResult> Preview(int id)
        {
            var quiz = await _context.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (quiz == null) return NotFound();

            var vm = new QuizPreviewVM
            {
                QuizTitle = quiz.Title,
                Questions = quiz.Questions.Select(q => new QuizQuestionVM
                {
                    QuestionText = q.QuestionText,
                    Options = q.Options.Select(o => new QuizOptionVM
                    {
                        OptionText = o.OptionText,
                        IsCorrect = false // nếu chỉ preview → không tiết lộ đáp án
                    }).ToList()
                }).ToList()
            };

            ViewData["CourseId"] = quiz.CourseId;
            return View(vm);
        }

    }
}
