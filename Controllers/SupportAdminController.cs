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
    [Authorize(Roles = "Admin,LGU")]
    public class SupportAdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SupportAdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /SupportAdmin/Index
        public async Task<IActionResult> Index()
        {
            var pendingChats = await _context.ChatSessions
                .Include(c => c.User)
                .Where(c => c.Status == ChatStatus.Active)
                .OrderByDescending(c => c.StartTime)
                .ToListAsync();
                
            return View(pendingChats);
        }
        
        // GET: /SupportAdmin/ManageFAQs
        public async Task<IActionResult> ManageFAQs()
        {
            var faqs = await _context.FAQs
                .OrderBy(f => f.Category)
                .ThenBy(f => f.DisplayOrder)
                .ToListAsync();
                
            return View(faqs);
        }
        
        // GET: /SupportAdmin/CreateFAQ
        public IActionResult CreateFAQ()
        {
            return View();
        }
        
        // POST: /SupportAdmin/CreateFAQ
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFAQ(FAQ faq)
        {
            if (ModelState.IsValid)
            {
                _context.FAQs.Add(faq);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "FAQ created successfully.";
                return RedirectToAction(nameof(ManageFAQs));
            }
            
            return View(faq);
        }
        
        // GET: /SupportAdmin/EditFAQ/5
        public async Task<IActionResult> EditFAQ(int id)
        {
            var faq = await _context.FAQs.FindAsync(id);
            
            if (faq == null)
            {
                return NotFound();
            }
            
            return View(faq);
        }
        
        // POST: /SupportAdmin/EditFAQ/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFAQ(int id, FAQ faq)
        {
            if (id != faq.Id)
            {
                return NotFound();
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(faq);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "FAQ updated successfully.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!FAQExists(faq.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                
                return RedirectToAction(nameof(ManageFAQs));
            }
            
            return View(faq);
        }
        
        // POST: /SupportAdmin/DeleteFAQ/5
        [HttpPost, ActionName("DeleteFAQ")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFAQConfirmed(int id)
        {
            var faq = await _context.FAQs.FindAsync(id);
            
            if (faq != null)
            {
                _context.FAQs.Remove(faq);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "FAQ deleted successfully.";
            }
            
            return RedirectToAction(nameof(ManageFAQs));
        }
        
        // GET: /SupportAdmin/ActiveChats
        public async Task<IActionResult> ActiveChats()
        {
            var activeChats = await _context.ChatSessions
                .Include(c => c.User)
                .Include(c => c.Messages)
                .Where(c => c.Status == ChatStatus.Active)
                .OrderByDescending(c => c.StartTime)
                .ToListAsync();
            
            return View(activeChats);
        }
        
        // GET: /SupportAdmin/ViewSession/5
        public async Task<IActionResult> ViewSession(int id)
        {
            var session = await _context.ChatSessions
                .Include(s => s.User)
                .Include(s => s.AssignedTo)
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null)
            {
                return NotFound();
            }

            // Check if user has permission to view this chat
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");
            var isLGU = User.IsInRole("LGU");

            if (!isAdmin && !(isLGU && session.AssignedToId == userId))
            {
                return Forbid();
            }

            // Get list of LGU users for assignment dropdown
            if (isAdmin)
            {
                ViewBag.LGUUsers = await _context.Users
                    .OfType<LGUUser>()
                    .Where(u => u.UserRole == UserRoleType.LGU)
                    .ToListAsync();
            }

            return View(session);
        }
        
        // POST: /SupportAdmin/SendMessage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int sessionId, string message)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId);
            
            if (session == null)
            {
                return NotFound();
            }
            
            if (session.Status == ChatStatus.Closed)
            {
                TempData["ErrorMessage"] = "This chat session has been closed.";
                return RedirectToAction(nameof(ViewSession), new { id = sessionId });
            }
            
            var chatMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = userId,
                Content = message,
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(ViewSession), new { id = sessionId });
        }
        
        // POST: /SupportAdmin/CloseChat
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseChat(int sessionId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId);
            
            if (session == null)
            {
                return NotFound();
            }
            
            session.Status = ChatStatus.Closed;
            session.EndTime = DateTime.Now;
            
            var closureMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = null,
                Content = "Chat session closed by support staff.",
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(closureMessage);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(ActiveChats));
        }
        
        // POST: /SupportAdmin/AssignChat
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignChat(int sessionId, string assignedToId)
        {
            var session = await _context.ChatSessions.FindAsync(sessionId);
            if (session == null)
            {
                return NotFound();
            }

            // Verify the assigned user is an LGU user
            var assignedUser = await _context.Users
                .OfType<LGUUser>()
                .FirstOrDefaultAsync(u => u.Id == assignedToId && u.UserRole == UserRoleType.LGU);

            if (assignedUser == null)
            {
                return BadRequest("Invalid LGU user selected");
            }

            session.AssignedToId = assignedToId;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Chat assigned successfully";
            return RedirectToAction(nameof(ViewSession), new { id = sessionId });
        }
        
        // GET: /SupportAdmin/ChatHistory
        public async Task<IActionResult> ChatHistory()
        {
            var history = await _context.ChatSessions
                .Include(c => c.User)
                .Include(c => c.AssignedTo)
                .OrderByDescending(c => c.StartTime)
                .ToListAsync();
                
            return View(history);
        }
        
        private bool FAQExists(int id)
        {
            return _context.FAQs.Any(e => e.Id == id);
        }
    }
} 