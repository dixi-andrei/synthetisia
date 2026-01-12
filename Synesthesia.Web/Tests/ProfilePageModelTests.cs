using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Moq;
using Synesthesia.Web.Data;
using Synesthesia.Web.Models;
using Synesthesia.Web.Pages;
using Xunit;

namespace Synesthesia.Web.Tests
{
    public class ProfilePageModelTests
    {
        private ApplicationDbContext GetInMemoryDb()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
                .Options;
            return new ApplicationDbContext(options);
        }

        private Mock<UserManager<AppUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<AppUser>>();
            return new Mock<UserManager<AppUser>>(
                store.Object, null, null, null, null, null, null, null, null
            );
        }

        private Mock<IWebHostEnvironment> MockEnv(string webRoot)
        {
            var m = new Mock<IWebHostEnvironment>();
            m.Setup(e => e.WebRootPath).Returns(webRoot);
            return m;
        }

        private ClaimsPrincipal CreateUser(string userId, string username)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, username)
            };
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        private PageContext CreatePageContext(ClaimsPrincipal user)
        {
            var httpContext = new DefaultHttpContext { User = user };
            return new PageContext { HttpContext = httpContext };
        }

        [Fact]
        public async Task OnGetAsync_UserAuthenticated_LoadsUserData()
        {
            // Arrange
            var db = GetInMemoryDb();
            var userManager = MockUserManager();
            var env = MockEnv(Path.GetTempPath());

            var testUser = new AppUser
            {
                Id = "user-123",
                UserName = "testuser",
                Email = "test@test.com",
                Bio = "Test bio",
                ProfilePicture = "/images/test.png"
            };

            var principal = CreateUser("user-123", "testuser");

            userManager.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(testUser);

            var model = new ProfileModel(userManager.Object, db, env.Object)
            {
                PageContext = CreatePageContext(principal)
            };

            // Act
            await model.OnGetAsync();

            // Assert
            Assert.Equal("testuser", model.Username);
            Assert.Equal("/images/test.png", model.ProfilePicture);
            Assert.Equal("Test bio", model.Bio);
        }

        [Fact]
        public async Task OnGetAsync_UserNotAuthenticated_ReturnsEarly()
        {
            // Arrange
            var db = GetInMemoryDb();
            var userManager = MockUserManager();
            var env = MockEnv(Path.GetTempPath());

            userManager.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync((AppUser?)null);

            var model = new ProfileModel(userManager.Object, db, env.Object)
            {
                PageContext = CreatePageContext(new ClaimsPrincipal())
            };

            // Act
            await model.OnGetAsync();

            // Assert
            Assert.Null(model.Username);
            Assert.Null(model.ProfilePicture);
        }

        [Fact]
        public async Task OnGetAsync_LoadsUserProjects()
        {
            // Arrange
            var db = GetInMemoryDb();
            var userManager = MockUserManager();
            var env = MockEnv(Path.GetTempPath());

            var userId = "user-123";
            var testUser = new AppUser
            {
                Id = userId,
                UserName = "testuser",
                Email = "test@test.com"
            };

            var audioFile = new AudioFile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FileName = "test.mp3",
                FilePath = "/uploads/audio/test.mp3",
                Format = "mp3"
            };

            var project = new FractalProject
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AudioId = audioFile.Id,
                Title = "Test Project",
                FractalType = "julia",
                SettingsJson = "{}"
            };

            await db.AudioFiles.AddAsync(audioFile);
            await db.FractalProjects.AddAsync(project);
            await db.SaveChangesAsync();

            var principal = CreateUser(userId, "testuser");
            userManager.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(testUser);

            var model = new ProfileModel(userManager.Object, db, env.Object)
            {
                PageContext = CreatePageContext(principal)
            };

            // Act
            await model.OnGetAsync();

            // Assert
            Assert.Single(model.Projects);
            Assert.Equal("Test Project", model.Projects[0].Title);
            Assert.Equal("julia", model.Projects[0].FractalType);
        }

        [Fact]
        public async Task OnPostDeleteProjectAsync_ValidProject_DeletesSuccessfully()
        {
            // Arrange
            var db = GetInMemoryDb();
            var userManager = MockUserManager();
            var tempDir = Path.Combine(Path.GetTempPath(), "syn-profile-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var env = MockEnv(tempDir);

            var userId = "user-123";
            var audioFile = new AudioFile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FileName = "test.mp3",
                FilePath = "uploads/audio/test.mp3",
                Format = "mp3"
            };

            var project = new FractalProject
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AudioId = audioFile.Id,
                Title = "Test Project",
                FractalType = "julia",
                SettingsJson = "{}"
            };

            await db.AudioFiles.AddAsync(audioFile);
            await db.FractalProjects.AddAsync(project);
            await db.SaveChangesAsync();

            // Create physical file
            var audioPath = Path.Combine(tempDir, audioFile.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
            await File.WriteAllTextAsync(audioPath, "fake audio");

            var principal = CreateUser(userId, "testuser");
            var model = new ProfileModel(userManager.Object, db, env.Object)
            {
                PageContext = CreatePageContext(principal)
            };

            // Act
            var result = await model.OnPostDeleteProjectAsync(project.Id);

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal(0, await db.FractalProjects.CountAsync());
            Assert.Equal(0, await db.AudioFiles.CountAsync());
            Assert.False(File.Exists(audioPath));

            // Cleanup
            try { Directory.Delete(tempDir, true); } catch { }
        }

        [Fact]
        public async Task OnPostDeleteProjectAsync_ProjectNotFound_ReturnsWithError()
        {
            // Arrange
            var db = GetInMemoryDb();
            var userManager = MockUserManager();
            var env = MockEnv(Path.GetTempPath());

            var userId = "user-123";
            var principal = CreateUser(userId, "testuser");
            var model = new ProfileModel(userManager.Object, db, env.Object)
            {
                PageContext = CreatePageContext(principal),
                TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                    new DefaultHttpContext(),
                    Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
            };

            // Act
            var result = await model.OnPostDeleteProjectAsync(Guid.NewGuid());

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(model.TempData["Error"]);
        }

        [Fact]
        public async Task OnPostDeleteProjectAsync_SharedAudio_KeepsAudioFile()
        {
            // Arrange
            var db = GetInMemoryDb();
            var userManager = MockUserManager();
            var env = MockEnv(Path.GetTempPath());

            var userId = "user-123";
            var audioFile = new AudioFile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FileName = "shared.mp3",
                FilePath = "/uploads/audio/shared.mp3",
                Format = "mp3"
            };

            var project1 = new FractalProject
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AudioId = audioFile.Id,
                Title = "Project 1",
                FractalType = "julia",
                SettingsJson = "{}"
            };

            var project2 = new FractalProject
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AudioId = audioFile.Id,
                Title = "Project 2",
                FractalType = "mandelbrot",
                SettingsJson = "{}"
            };

            await db.AudioFiles.AddAsync(audioFile);
            await db.FractalProjects.AddRangeAsync(project1, project2);
            await db.SaveChangesAsync();

            var principal = CreateUser(userId, "testuser");
            var model = new ProfileModel(userManager.Object, db, env.Object)
            {
                PageContext = CreatePageContext(principal),
                TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                    new DefaultHttpContext(),
                    Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
            };

            // Act
            await model.OnPostDeleteProjectAsync(project1.Id);

            // Assert
            Assert.Equal(1, await db.FractalProjects.CountAsync());
            Assert.Equal(1, await db.AudioFiles.CountAsync()); // Audio should still exist
            Assert.Contains("kept", model.TempData["Success"]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }
}