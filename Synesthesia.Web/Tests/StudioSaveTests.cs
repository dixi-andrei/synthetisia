using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    public class StudioSaveTests
    {
        private ApplicationDbContext GetInMemoryDb()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
                .Options;
            return new ApplicationDbContext(options);
        }

        private Mock<IWebHostEnvironment> MockEnv(string webRoot)
        {
            var m = new Mock<IWebHostEnvironment>();
            m.Setup(e => e.WebRootPath).Returns(webRoot);
            return m;
        }

        private ClaimsPrincipal AuthenticatedPrincipal(string userId)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            }, "TestAuth");
            return new ClaimsPrincipal(identity);
        }

        private PageContext MakePageContextWithUser(ClaimsPrincipal user)
        {
            var ctx = new DefaultHttpContext();
            ctx.User = user ?? new ClaimsPrincipal();
            return new PageContext { HttpContext = ctx };
        }

        [Fact]
        public async Task OnPostSaveToProfileAsync_ValidData_SavesProjectAndAudio()
        {
            // Arrange
            var db = GetInMemoryDb();
            var temp = Path.Combine(Path.GetTempPath(), "syn-studio-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(temp);
            var audioDir = Path.Combine(temp, "uploads", "audio");
            Directory.CreateDirectory(audioDir);

            var envMock = MockEnv(temp);
            var model = new StudioModel(envMock.Object, db);
            model.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));

            // Create a fake audio file
            var audioFileName = "test.mp3";
            var audioPath = Path.Combine(audioDir, audioFileName);
            await File.WriteAllTextAsync(audioPath, "FAKE_AUDIO");

            var settingsJson = @"{
                ""fractalType"": ""julia"",
                ""iterations"": 300,
                ""bassStrength"": 1.5
            }";

            // Act
            var result = await model.OnPostSaveToProfileAsync(
                audioPath: $"/uploads/audio/{audioFileName}",
                originalFileName: audioFileName,
                settingsJson: settingsJson,
                fractalType: "julia"
            );

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            dynamic? data = jsonResult.Value;
            Assert.NotNull(data);
            Assert.True(data?.success);

            // Verify database
            var savedProject = await db.FractalProjects.FirstOrDefaultAsync();
            Assert.NotNull(savedProject);
            Assert.Equal("julia", savedProject.FractalType);
            Assert.Contains("iterations", savedProject.SettingsJson);
            Assert.Equal("user-42", savedProject.UserId);

            var savedAudio = await db.AudioFiles.FirstOrDefaultAsync();
            Assert.NotNull(savedAudio);
            Assert.Equal(audioFileName, savedAudio.FileName);

            try { Directory.Delete(temp, true); } catch { }
        }

        [Fact]
        public async Task OnPostSaveToProfileAsync_NotAuthenticated_ReturnsError()
        {
            // Arrange
            var db = GetInMemoryDb();
            var envMock = MockEnv(Path.GetTempPath());
            var model = new StudioModel(envMock.Object, db);
            model.PageContext = MakePageContextWithUser(new ClaimsPrincipal());

            // Act
            var result = await model.OnPostSaveToProfileAsync(
                audioPath: "/uploads/audio/test.mp3",
                originalFileName: "test.mp3",
                settingsJson: "{}",
                fractalType: "julia"
            );

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            dynamic? data = jsonResult.Value;
            Assert.NotNull(data);
            Assert.False(data?.success);
            Assert.Contains("not authenticated", data?.message.ToString() ?? "");
        }

        [Fact]
        public async Task OnPostSaveToProfileAsync_MissingAudioPath_ReturnsError()
        {
            // Arrange
            var db = GetInMemoryDb();
            var envMock = MockEnv(Path.GetTempPath());
            var model = new StudioModel(envMock.Object, db);
            model.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));

            // Act
            var result = await model.OnPostSaveToProfileAsync(
                audioPath: "",
                originalFileName: "test.mp3",
                settingsJson: "{}",
                fractalType: "julia"
            );

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            dynamic? data = jsonResult.Value;
            Assert.NotNull(data);
            Assert.False(data?.success);
            Assert.Contains("audio path", data?.message.ToString().ToLower() ?? "");
        }

        [Fact]
        public async Task OnPostSaveToProfileAsync_AudioFileNotFound_ReturnsError()
        {
            // Arrange
            var db = GetInMemoryDb();
            var temp = Path.Combine(Path.GetTempPath(), "syn-studio-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(temp);

            var envMock = MockEnv(temp);
            var model = new StudioModel(envMock.Object, db);
            model.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));

            // Act
            var result = await model.OnPostSaveToProfileAsync(
                audioPath: "/uploads/audio/nonexistent.mp3",
                originalFileName: "nonexistent.mp3",
                settingsJson: "{}",
                fractalType: "julia"
            );

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            dynamic? data = jsonResult.Value;
            Assert.NotNull(data);
            Assert.False(data?.success);
            Assert.Contains("not found", data?.message.ToString().ToLower() ?? "");

            try { Directory.Delete(temp, true); } catch { }
        }

        [Fact]
        public async Task OnPostSaveToProfileAsync_ExistingAudioFile_ReusesAudio()
        {
            // Arrange
            var db = GetInMemoryDb();
            var temp = Path.Combine(Path.GetTempPath(), "syn-studio-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(temp);
            var audioDir = Path.Combine(temp, "uploads", "audio");
            Directory.CreateDirectory(audioDir);

            var envMock = MockEnv(temp);

            // Create existing audio file
            var audioFileName = "existing.mp3";
            var audioPath = Path.Combine(audioDir, audioFileName);
            await File.WriteAllTextAsync(audioPath, "FAKE_AUDIO");

            var existingAudio = new AudioFile
            {
                UserId = "user-42",
                FileName = audioFileName,
                FilePath = $"/uploads/audio/{audioFileName}",
                Format = "mp3"
            };
            await db.AudioFiles.AddAsync(existingAudio);
            await db.SaveChangesAsync();

            var model = new StudioModel(envMock.Object, db);
            model.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));

            // Act
            var result = await model.OnPostSaveToProfileAsync(
                audioPath: $"/uploads/audio/{audioFileName}",
                originalFileName: audioFileName,
                settingsJson: "{}",
                fractalType: "mandelbrot"
            );

            // Assert
            var jsonResult = Assert.IsType<JsonResult>(result);
            dynamic? data = jsonResult.Value;
            Assert.NotNull(data);
            Assert.True(data?.success);

            // Should still only have 1 audio file
            var audioCount = await db.AudioFiles.CountAsync();
            Assert.Equal(1, audioCount);

            // But should have 1 project
            var projectCount = await db.FractalProjects.CountAsync();
            Assert.Equal(1, projectCount);

            var project = await db.FractalProjects.FirstAsync();
            Assert.Equal(existingAudio.Id, project.AudioId);

            try { Directory.Delete(temp, true); } catch { }
        }

        [Fact]
        public async Task OnPostSaveToProfileAsync_GeneratesDefaultTitle()
        {
            // Arrange
            var db = GetInMemoryDb();
            var temp = Path.Combine(Path.GetTempPath(), "syn-studio-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(temp);
            var audioDir = Path.Combine(temp, "uploads", "audio");
            Directory.CreateDirectory(audioDir);

            var envMock = MockEnv(temp);
            var model = new StudioModel(envMock.Object, db);
            model.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));

            var audioFileName = "mysong.mp3";
            var audioPath = Path.Combine(audioDir, audioFileName);
            await File.WriteAllTextAsync(audioPath, "FAKE_AUDIO");

            // Act
            var result = await model.OnPostSaveToProfileAsync(
                audioPath: $"/uploads/audio/{audioFileName}",
                originalFileName: audioFileName,
                settingsJson: "{}",
                fractalType: "julia"
            );

            // Assert
            var project = await db.FractalProjects.FirstOrDefaultAsync();
            Assert.NotNull(project);
            Assert.Contains("Julia", project.Title);
            Assert.Contains("mysong", project.Title);

            try { Directory.Delete(temp, true); } catch { }
        }

        [Fact]
        public async Task OnPostSaveToProfileAsync_DifferentFractalTypes_SaveCorrectly()
        {
            // Arrange
            var db = GetInMemoryDb();
            var temp = Path.Combine(Path.GetTempPath(), "syn-studio-tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(temp);
            var audioDir = Path.Combine(temp, "uploads", "audio");
            Directory.CreateDirectory(audioDir);

            var envMock = MockEnv(temp);

            var audioFileName = "test.mp3";
            var audioPath = Path.Combine(audioDir, audioFileName);
            await File.WriteAllTextAsync(audioPath, "FAKE_AUDIO");

            // Test Julia
            var juliaModel = new StudioModel(envMock.Object, db);
            juliaModel.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));
            await juliaModel.OnPostSaveToProfileAsync(
                $"/uploads/audio/{audioFileName}", audioFileName, "{}", "julia");

            // Test Mandelbrot
            var mandelModel = new StudioModel(envMock.Object, db);
            mandelModel.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));
            await mandelModel.OnPostSaveToProfileAsync(
                $"/uploads/audio/{audioFileName}", audioFileName, "{}", "mandelbrot");

            // Test Mandelbulb
            var bulbModel = new StudioModel(envMock.Object, db);
            bulbModel.PageContext = MakePageContextWithUser(AuthenticatedPrincipal("user-42"));
            await bulbModel.OnPostSaveToProfileAsync(
                $"/uploads/audio/{audioFileName}", audioFileName, "{}", "mandelbulb");

            // Assert
            var projects = await db.FractalProjects.ToListAsync();
            Assert.Equal(3, projects.Count);
            Assert.Contains(projects, p => p.FractalType == "julia");
            Assert.Contains(projects, p => p.FractalType == "mandelbrot");
            Assert.Contains(projects, p => p.FractalType == "mandelbulb");

            try { Directory.Delete(temp, true); } catch { }
        }
    }
}