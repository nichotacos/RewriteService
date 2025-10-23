using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace RewriteService {
    public class LoggerService {
        private readonly IWebHostEnvironment _env;

        public LoggerService(IWebHostEnvironment env) {
            _env = env;
        }

        public void TLog(string content, int tipe = 0) {
            // Create log directory
            string logDir = Path.Combine(_env.ContentRootPath, "log");
            Directory.CreateDirectory(logDir);

            // Build log filename
            string fileName = tipe == 3
                ? $"errlog-{DateTime.Now:MMM-yyyy}.txt"
                : $"log-{DateTime.Now:MMM-yyyy}.txt";

            string filePath = Path.Combine(logDir, fileName);

            int countTry = 0;

            while (countTry < 5) {
                try {
                    string logLine = $"{DateTime.Now:dd-MM-yyyy} {content}";

                    if (!File.Exists(filePath)) {
                        using (StreamWriter sw = File.CreateText(filePath)) {
                            sw.WriteLine(logLine); 
                        }
                    } else {
                        using (StreamWriter sw = File.AppendText(filePath)) {
                            sw.WriteLine(logLine);
                        }
                    }

                    break;
                } catch {
                    countTry++;
                }
            }
        }

    }
}
