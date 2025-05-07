using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using THYNK.Data;
using THYNK.Models;

namespace THYNK.Controllers
{
    [Authorize]
    public class SupportController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SupportController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Support/Index
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Get active chat session if exists
            var activeSession = await _context.ChatSessions
                .Include(c => c.Messages)
                .Where(c => c.UserId == userId && c.Status != ChatStatus.Closed)
                .OrderByDescending(c => c.StartTime)
                .FirstOrDefaultAsync();
            
            // Get FAQs
            var faqs = await _context.FAQs
                .Where(f => f.IsPublished)
                .OrderBy(f => f.Category)
                .ThenBy(f => f.DisplayOrder)
                .ToListAsync();
            
            // Group FAQs by category
            var groupedFaqs = faqs.GroupBy(f => f.Category).ToList();
            
            ViewBag.ActiveSession = activeSession;
            ViewBag.GroupedFaqs = groupedFaqs;
            
            return View();
        }
        
        // POST: /Support/StartChat
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartChat(string title, string category, string initialMessage)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Check if user already has an active chat
            var existingActiveChat = await _context.ChatSessions
                .AnyAsync(c => c.UserId == userId && c.Status != ChatStatus.Closed);
            
            if (existingActiveChat)
            {
                TempData["ErrorMessage"] = "You already have an active chat session.";
                return RedirectToAction(nameof(Index));
            }
            
            // Create new chat session
            var chatSession = new ChatSession
            {
                UserId = userId,
                Title = string.IsNullOrEmpty(title) ? "Chat Support Session" : title,
                Category = category,
                StartTime = DateTime.Now,
                Status = ChatStatus.Active
            };
            
            _context.ChatSessions.Add(chatSession);
            await _context.SaveChangesAsync();
            
            // Add initial message if provided
            if (!string.IsNullOrEmpty(initialMessage))
            {
                var message = new ChatMessage
                {
                    ChatSessionId = chatSession.Id,
                    SenderId = userId,
                    Content = initialMessage,
                    Timestamp = DateTime.Now
                };
                
                _context.ChatMessages.Add(message);
                
                // Add automated welcome response
                var welcomeMessage = new ChatMessage
                {
                    ChatSessionId = chatSession.Id,
                    SenderId = null,
                    Content = "Thank you for reaching out! Our support team will respond to your message as soon as possible. In the meantime, you might find an answer to your question in our FAQ section.",
                    Timestamp = DateTime.Now.AddSeconds(1)
                };
                
                _context.ChatMessages.Add(welcomeMessage);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(Index));
        }
        
        // POST: /Support/SendMessage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int sessionId, string message)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Check if session exists and belongs to user
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId && c.UserId == userId);
            
            if (session == null)
            {
                return NotFound();
            }
            
            if (session.Status == ChatStatus.Closed)
            {
                TempData["ErrorMessage"] = "This chat session has been closed.";
                return RedirectToAction(nameof(Index));
            }
            
            // Add message
            var chatMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = userId,
                Content = message,
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(Index));
        }
        
        // POST: /Support/CloseChat
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseChat(int sessionId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Check if session exists and belongs to user
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId && c.UserId == userId);
            
            if (session == null)
            {
                return NotFound();
            }
            
            // Close the session
            session.Status = ChatStatus.Closed;
            session.EndTime = DateTime.Now;
            
            // Add system message indicating session closure
            var closureMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = null,
                Content = "Chat session closed by user.",
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(closureMessage);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(Index));
        }
        
        // GET: /Support/ViewSession/5
        public async Task<IActionResult> ViewSession(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Check permissions based on user role
            bool isAdminOrLGU = User.IsInRole("Admin") || User.IsInRole("LGU");
            
            // Get session with messages
            var session = await _context.ChatSessions
                .Include(c => c.Messages.OrderBy(m => m.Timestamp))
                .Include(c => c.User)
                .Include(c => c.AssignedTo)
                .FirstOrDefaultAsync(c => c.Id == id && (c.UserId == userId || isAdminOrLGU));
            
            if (session == null)
            {
                return NotFound();
            }
            
            return View(session);
        }
        
        // GET: /Support/ChatHistory
        public async Task<IActionResult> ChatHistory()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Get all user's chat sessions
            var sessions = await _context.ChatSessions
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.StartTime)
                .ToListAsync();
            
            return View(sessions);
        }
    }
} 