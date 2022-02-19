//////////////////////////////////////////////////////////////////////////////
//                             ПЕРЕМЕННЫЕ ДЛЯ ВИДЕО
//////////////////////////////////////////////////////////////////////////////
Random rnd = new Random();

string mp3File = project.Variables["mp3_file"].Value;
string audioName = project.Variables["audio_name"].Value;
string tempDir = project.Variables["temp_dir"].Value;
string templatePath = project.Directory + @"\video_template\";
var articleImagesList = project.Lists["article_images"];

string mp4FileTemp = tempDir + "video_temp.mp4";

// Настройки
int width = 1280;
int height = 720;
double fps = 30;
double slideDuration = 20;
double transitionDuration = 2;
string zoomSpeed = "2"; // 1=SLOWEST, 2=SLOW, 3=MODERATE, 4=FASTER, 5=FASTEST, ...
string zoomScale = "0.0002";
string voiceVolume = "1.5";
string musicVolume = "0.2";

// Текст
bool useText = false;
int textFrameHeight = 60;
int textFrameY = 660;
int textY = 678;
string textFont = @"C\:\\Windows\\Fonts\\arial.ttf";
string text = "Ссылка в описании";
int textFontSize = 30;
string textFontColor = "red";
int textSpeed = 3;                // 1=FASTEST, 2=FASTER, 3=MODERATE, 4=SLOW, 5=SLOWEST
string backgroundColor = "yellow";
int direction = 2;                 // 1=LEFT TO RIGHT, 2=RIGHT TO LEFT

// Использовать фоновую музыку
bool useBackgroundMusic = true;

////////////////////////////////////////////////////
//        ПОДГОТОВКА КАРТИНОК
////////////////////////////////////////////////////

List<string> imagesList = new List<string>();

ImageFactory imageFactory = new ImageFactory(preserveExifData:true);
// настраиваем параметры конвертации изображения
ISupportedImageFormat format = new ImageProcessor.Imaging.Formats.JpegFormat { Quality = 100 }; // Устанавливаем качество фото на выходе

int num = 1;
foreach(string imgPath in articleImagesList) {
	//обрабатываем изображение и сохраняем результат в заданный файл
	string newFilePath = tempDir + num + ".jpg";
	if (!File.Exists(newFilePath)) {
		//imageFactory.Load(imgPath).Format(format).Save(newFilePath); // Сохранение в новом формате в папку темп
		ResizeLayer resize = new ResizeLayer(new Size(1280, 720), ResizeMode.Crop, AnchorPosition.Center, true);
		imageFactory.Load(imgPath).Resize(resize).Format(format).Save(newFilePath);
	}
	imagesList.Add(newFilePath);
	num++;
}

project.SendInfoToLog("Подготовили картинки", true);

//////////////////////////////////////////////////////////////////////////////
//                             ЗАЦИКЛИВАНИЕ СЛАЙДОВ
//////////////////////////////////////////////////////////////////////////////

project.SendInfoToLog("Начинаем генерацию видео с помощью ffmpeg", true);

// Продолжительность голоса = продолжительность видео
double totalDuration = TagLib.File.Create(mp3File).Properties.Duration.TotalSeconds;

// Длительность слайда по количеству картинок
double slideCount = imagesList.Count;
double slideDurationImage = (totalDuration - (slideCount - 1) * transitionDuration) / slideCount; 

// Если длительность слайда больше чем указано в настройках то увеличиваем кол-во слайдов
if (slideDurationImage > slideDuration) {
	slideCount = Math.Ceiling(totalDuration / slideDuration);
	slideDuration = (totalDuration - (slideCount - 1) * transitionDuration) / slideCount;
} else {
	slideDuration = slideDurationImage;
}

// Добавляем слайдов
int s = 0;
while (imagesList.Count < slideCount) {
	if (s >= imagesList.Count) s = 0;
	imagesList.Add(imagesList[s++]);
}

//////////////////////////////////////////////////////////////////////////////
//                             ВСПОМОГАТЕЛЬНЫЕ ПЕРЕМЕННЫЕ
//////////////////////////////////////////////////////////////////////////////

// frame counts
double transitionFrameCount = transitionDuration * fps;
double slideFrameCount = slideDuration * fps;
double totalFrameCount = totalDuration * fps;

string transitionFrameCountStr = transitionFrameCount.ToString().Replace(',','.');
string slideFrameCountStr = slideFrameCount.ToString().Replace(',','.');
string totalFrameCountStr = totalFrameCount.ToString().Replace(',','.');

string fpsStr = fps.ToString().Replace(',','.');
string slideDurationStr = slideDuration.ToString().Replace(',','.');
string transitionDurationStr = transitionDuration.ToString().Replace(',','.');

//////////////////////////////////////////////////////////////////////////////
//                             НАСТРОЙКА FFMPEG
//////////////////////////////////////////////////////////////////////////////

ProcessStartInfo ffmpegInfo = new ProcessStartInfo();
ffmpegInfo.FileName = project.Directory + @"\ffmpeg.exe";
ffmpegInfo.WindowStyle = ProcessWindowStyle.Hidden;

//////////////////////////////////////////////////////////////////////////////
//                             НАЛОЖЕНИЕ ФОНОВОЙ МУЗЫКИ
//////////////////////////////////////////////////////////////////////////////

if(useBackgroundMusic) {
	string script = "";
	
	// Копируем музыку в папку темп
	var musicList = Directory.GetFiles(templatePath + "music").ToList();
	string music = musicList[rnd.Next(0, musicList.Count)];
	
	string musicFile = tempDir + "background_music.mp3";
	if(!File.Exists(musicFile)) File.Copy(music, musicFile);

	// voice
	script += " -i " + mp3File;
	
	// background music
	script += " -i " + musicFile;

	// Background music
	mp3File = tempDir + "voice_temp.mp3";
	script += string.Format(" -filter_complex \"[0:a]volume={0}[voice];[1:a]volume={1}[music];[voice][music]amix=inputs=2:duration=first[audio]\" -map [audio] {2}", voiceVolume, musicVolume, mp3File);
	
	// Запуск ffmpeg
	ffmpegInfo.Arguments = script;
	Process videoProcess1 = Process.Start(ffmpegInfo);
	project.SendInfoToLog("Делаем видео с помощью ffmpeg");
	videoProcess1.WaitForExit(1200000);
}

//////////////////////////////////////////////////////////////////////////////
//                             ПОДГОТОВКА СКРИПТА FFMPEG
//////////////////////////////////////////////////////////////////////////////

// # 1. START COMMAND
string fullScript = "-y ";

// # 2. ADD INPUTS
foreach(string photo in imagesList) {
    fullScript += " -loop 1 -i " + photo;
}

// voice
fullScript += " -i " + mp3File;

// # 3. START FILTER COMPLEX
fullScript += " -filter_complex \"";

// # 4. PREPARING SCALED INPUTS & FADE IN/OUT PARTS
for (int i = 0; i < imagesList.Count; i++) {
    fullScript += string.Format(@"[{0}:v]setpts=PTS-STARTPTS,scale=w='if(gte(iw/ih,{2}/{3}),-1,{2})':h='if(gte(iw/ih,{2}/{3}),{3},-1)',crop={2}:{3},setsar=sar=1/1,format=rgba,split=2[stream{1}out1][stream{1}out2];", i, i + 1, width, height);

    fullScript += string.Format(@"[stream{0}out1]trim=duration={1},select=lte(n\,{2}),split=2[stream{0}in][stream{0}out];", i + 1, transitionDurationStr, transitionFrameCountStr);
	fullScript += string.Format(@"[stream{0}out2]trim=duration={1},select=lte(n\,{2})[stream{0}];", i + 1, slideDurationStr, slideFrameCountStr);

    fullScript += string.Format(@"[stream{0}in]fade=t=in:s=0:n={1}[stream{0}fadein];", i + 1, transitionFrameCountStr);
    fullScript += string.Format(@"[stream{0}out]fade=t=out:s=0:n={1}[stream{0}fadeout];", i + 1, transitionFrameCountStr);
}

// # 5. ZOOM & PAN EACH STREAM
for (int j = 0; j < imagesList.Count; j++) {
    int positionNumber = rnd.Next(0, 5);
	
	string positionFormula = "";
	switch (positionNumber) {
    	case 0:
            positionFormula = "x='iw/2-(iw/zoom/2)':y=0";
			break;
        case 1:
            positionFormula = "x='iw/2':y='(ih/zoom/2)'";
			break;
        case 2:
            positionFormula = string.Format("x='{0}-(iw/zoom/2)':y='-{1}-(ih/zoom/2)'", width, height);
			break;
        case 3:
            positionFormula = "x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'";
			break;			
        case 4:
            positionFormula = string.Format("x='-(iw/zoom/2)':y='ih/2-{0}'", height);
			break;
    }
	
	string zoomMode = "";
	switch(rnd.Next(0, 2)) {
		case 0:
			zoomMode = f"min(pzoom+{zoomScale}*{zoomSpeed},2)"
			break;
		case 1:
			zoomMode = f"1.5-in*{zoomScale}*{zoomSpeed}"
			break;
	}
	
	fullScript += string.Format(@"[stream{0}fadein][stream{0}][stream{0}fadeout]concat=n=3:v=1:a=0,scale={2}*5:-1,zoompan=z='{5}':d=1:{1}:fps={4}:s={2}x{3}[stream{0}panning];", j + 1, positionFormula, width, height, fpsStr, zoomMode);
}

// 8. BEGIN CONCAT
for (int k = 1; k <= imagesList.Count; k++) {
    fullScript += string.Format("[stream{0}panning]", k);
}

if (useText) {
	// 9. END CONCAT
	fullScript += string.Format("concat=n={0}:v=1:a=0,format=yuv420p[videowithouttext];", imagesList.Count);
	
	// 10. PREPARE TEXT BOX
	fullScript += string.Format("[videowithouttext]drawbox=x=0:y={0}:w={1}:h={2}:color={4}@0.8:t={2},trim=duration={3}[videowithbox];", textFrameY, width, textFrameHeight, totalDuration.ToString().Replace(",", "."), backgroundColor);

	// 11. OVERLAY TEXT ON TOP OF SLIDESHOW
	switch (direction) {
	    case 1:
	        fullScript += string.Format("[videowithbox]drawtext=fontfile='{0}':text='{1}':fontsize={2}:fontcolor={3}:x='-text_w + mod(t/{4}*(({5}+text_w)/{4}),{5}+text_w)':y={6}[video]", textFont, text, textFontSize, textFontColor, textSpeed, width, textY);
			break;
		case 2:
	        fullScript += string.Format("[videowithbox]drawtext=fontfile='{0}':text='{1}':fontsize={2}:fontcolor={3}:x='{5} - mod(t/{4}*(({5}+text_w)/{4}),{5}+text_w)':y={6}[video]", textFont, text, textFontSize, textFontColor, textSpeed, width, textY);
	    	break;
	}
} else {
	// 9. END CONCAT
	fullScript += string.Format("concat=n={0}:v=1:a=0,format=yuv420p[video]", imagesList.Count);
}

// END
fullScript += string.Format("\" -map [video] -vsync 2 -async 1 -rc-lookahead 0 -g 0 -profile:v main -level 42 -c:v libx264 -c:a aac -map {2}:a -shortest -r {0} {1}", fpsStr, mp4FileTemp, imagesList.Count);


//////////////////////////////////////////////////////////////////////////////
//                          ДЕЛАЕМ ВИДЕО
//////////////////////////////////////////////////////////////////////////////

// Запуск ffmpeg
ffmpegInfo.Arguments = fullScript;
Process videoProcess = Process.Start(ffmpegInfo);
project.SendInfoToLog("Делаем видео с помощью ffmpeg");
videoProcess.WaitForExit(12000000);

project.SendInfoToLog("Видео сгенерировано", true);