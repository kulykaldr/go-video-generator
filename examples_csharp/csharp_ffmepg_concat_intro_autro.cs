string youtubeLogin = project.Variables["youtube_login"].Value;
string templatePath = project.Directory + @"\video_template\";
string introPath = templatePath + youtubeLogin + "_intro.mp4";
string outroPath = templatePath + youtubeLogin + "_outro.mp4";

string audioName = project.Variables["audio_name"].Value;
string tempDir = project.Variables["temp_dir"].Value;

string mp4FileTemp = tempDir + "video_temp.mp4";

string title = project.Variables["title"].Value;
title = Regex.Replace(title, @"[^а-яА-ЯёЁa-zA-Z\d\s]", "").Trim();
string mp4File = tempDir + title + ".mp4";

/*
string title = project.Variables["title"].Value;
title = Regex.Replace(title, @"[^а-яА-ЯёЁa-zA-Z\d\s]", "").Trim();
string mp4File = tempDir + @"\" + title + ".mp4";
*/

//////////////////////////////////////////////////////////////////////////////
//                          ДОБАВЛЯЕМ ИНТРО И ОУТРО
//////////////////////////////////////////////////////////////////////////////

project.SendInfoToLog("Начали добавлять интро и аутро", true);

project.Variables["video_file"].Value = mp4File;

// Если нет ни интро ни аутро, то пропускаем добавление
if (!File.Exists(introPath) || !File.Exists(outroPath)) {
	File.Move(mp4FileTemp, mp4File);
	return "ok";
}

int concatNum = 2;
string map = "[0:v:0][0:a:0][1:v:0][1:a:0]";

string intro = "";
if (File.Exists(introPath)) intro = "-i " + introPath;

string outro = "";
if (File.Exists(outroPath)) { 
	outro = "-i " + outroPath;
	map += "[2:v:0][2:a:0]";
	concatNum++;
}

string concat = string.Format("{0} {1} {2} -filter_complex \"{4}concat=n={5}:v=1:a=1[outv][outa]\" -map [outv] -map [outa] \"{3}\"", intro, "-i " + mp4FileTemp, outro, mp4File, map, concatNum);

// Настройка ffmpeg
ProcessStartInfo ffmpegInfo = new ProcessStartInfo();
ffmpegInfo.FileName = project.Directory + @"\ffmpeg.exe";
ffmpegInfo.WindowStyle = ProcessWindowStyle.Hidden;

// Запуск ffmpeg
ffmpegInfo.Arguments = concat;
Process videoProcess = Process.Start(ffmpegInfo);
project.SendInfoToLog("Делаем видео с помощью ffmpeg");
videoProcess.WaitForExit(1200000);

project.SendInfoToLog("Закончили добавлять интро и аутро", true);