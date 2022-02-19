var rnd = new Random();

// width & height of video
int videoWidth = Convert.ToInt32(project.Variables["cfg_video_width"].Value);
int videoHeight = Convert.ToInt32(project.Variables["cfg_video_height"].Value);

var videoList = project.Lists["used_videos"];

// Создание экземпляра ffprobe & ffmpeg
ZHelper ffprobe = new ZHelper(project, instance);
ZHelper ffmpeg = new ZHelper(project, instance);

string videoInfo = "";

int src = 0;
string sourceScript = "";
string filterScript = "";
string codecsScript = "";
string finalVideoSource = "";
string finalAudioSource = "";

sourceScript += "-y "; // запрос на перезапись файла

// Добавление ресурсов видео для работы
foreach (string video in videoList) {
	sourceScript += $"-i \"{video}\" ";
}
src = videoList.Count - 1;

// добавляем filter complex
filterScript += "-filter_complex \"";

// Увеличиваем или уменьшаем высоту каждого видео до указанного в настройках
int i;
for (i = 0; i < videoList.Count; i++) {
	filterScript += $"[{i}:v]scale=-1:{videoHeight}[v{i}];";
}

finalVideoSource = "v";


// Наложение фильтра уникализации поверх видео
string noiseRatio = ZHelper.DoubleToString(1 - Convert.ToDouble(project.Variables["cfg_video_noise_ratio"].Value) / 100);
string noiseOpacity = ZHelper.DoubleToString(Convert.ToDouble(project.Variables["cfg_video_noise_opacity"].Value) / 100);
bool useVideoNoise = Convert.ToBoolean(project.Variables["cfg_use_video_noise"].Value);

if (useVideoNoise) {
	for (i = 0; i < videoList.Count; i++) {
		videoInfo = ffprobe.GetVideoInfoJson(videoList[i]);
		project.Json.FromString(videoInfo);
		
		filterScript += $"life=ratio={noiseRatio}:s={project.Json.streams[0].width}x{project.Json.streams[0].height}:rate=30,trim=duration={project.Json.streams[0].duration},split[life{i}][lifeс{i}];";
		filterScript += $"[life{i}][lifeс{i}]alphamerge,format=rgba,colorchannelmixer=aa={noiseOpacity}[lifekeyed{i}];";
		filterScript += $"[{finalVideoSource}{i}][lifekeyed{i}]overlay=eof_action=endall[noisev{i}];";
	}
	
	finalVideoSource = "noisev";
}

// Сделать зеркальное отображение по горизонтали
bool useVideoMirror = Convert.ToBoolean(project.Variables["cfg_use_video_mirror"].Value);
if (useVideoMirror) {
	for (i = 0; i < videoList.Count; i++) {
		filterScript += $"[{finalVideoSource}{i}]hflip[flipv{i}];";
	}
	
	finalVideoSource = "flipv";
}


// Накладываем на статичный фон все видео
string overlayMode = project.Variables["cfg_overlay_mode"].Value;
string videoSizeBorder = (videoHeight - Convert.ToInt32(project.Variables["cfg_video_border"].Value) * 2).ToString();
string bgBlurLevel = project.Variables["cfg_bg_blur_level"].Value;

List<string> overlayImgList = Directory.GetFiles(project.Variables["cfg_overlay_dir"].Value).ToList();

if (overlayMode == "статичный фон" || overlayMode == "размытый видеофон" && overlayImgList.Count > 0) {
	if (overlayMode == "статичный фон") {
		// Добавляем русурс статического фона для видео
		overlayImgList.Shuffle();
		sourceScript += $"-i \"{overlayImgList[0]}\" ";
		src++;
		
	}
	
	// Перебираем все ресурсы видео и добавляем в фильтр
	for (i = 0; i < videoList.Count; i++) {
		// Ресайзим исходное видео под размер с рамкой
		filterScript += $"[{finalVideoSource}{i}]scale=-1:{videoSizeBorder}[inScrBorder{i}];";
		
		// Создаем стрим с размытым фоном
		if (overlayMode == "размытый видеофон") {
			filterScript += $"[{i}:v]scale={videoWidth}:-1:flags=lanczos,setsar=1,gblur=sigma={bgBlurLevel},crop={videoWidth}:{videoHeight}[bg{i}];";
		}
		
		if (overlayMode == "статичный фон") {
			filterScript += $"[{src}:v]scale={videoWidth}:{videoHeight}[bg{i}];";
		}
		
		filterScript += $"[bg{i}][inScrBorder{i}]overlay=(W-w)/2:(H-h)/2[overlay{i}];";
	}
	
	finalVideoSource = "overlay";
}


// Добавление товаров на видео
bool addProducts = Convert.ToBoolean(project.Variables["cfg_ads_add_products"].Value);
List<string> productsImgList = Directory.GetFiles(project.Variables["cfg_ads_products_folder"].Value).ToList();
string adsZoneWidth = project.Variables["cfg_ads_zone_width"].Value;
bool productRepeat = Convert.ToBoolean(project.Variables["cfg_ads_product_repeat"].Value);
int productsSrc = src + 1;
productsImgList.Shuffle();

if (addProducts && overlayMode == "статичный фон") {
	int productIndex = 0;
	for (i = 0; i < videoList.Count; i++) {
		 // Если количество видео больше чем кол-во товаро то индес сбрасываем на начальное значение
		if (productIndex >= productsImgList.Count) productIndex = 0;
		sourceScript += $"-i \"{productsImgList[productIndex]}\" ";
		productIndex++;
		src++;
	
		filterScript += $"[{finalVideoSource}{i}][{src}:v]overlay=({adsZoneWidth}-w)/2:(H-h)/2[product{i}];";
		
		if (productRepeat) {
			filterScript += $"[product{i}][{src}:v]overlay=(W-{adsZoneWidth})+({adsZoneWidth}-w)/2:(H-h)/2[productr{i}];";
		}
	}
	
	finalVideoSource = "product";
	if (productRepeat) finalVideoSource = "productr";
}


// Фоновые звуки и музыка
string musicDirPath = project.Variables["cfg_bg_music_dirpath"].Value;
List<string> backgroundMusicList = new List<string>();
if (!string.IsNullOrWhiteSpace(musicDirPath)) backgroundMusicList = Directory.GetFiles(musicDirPath).ToList();

string bgMusic = project.Variables["cfg_bg_music_mode"].Value;
string audioMute = project.Variables["cfg_audio_mute"].Value;
string bgMusicVolume = ZHelper.DoubleToString(Convert.ToDouble(project.Variables["cfg_bg_music_volume"].Value) / 100);

// Убрать звук из видео, если в названии есть "_mute"
for (i = 0; i < videoList.Count; i++) {
	if (audioMute == "Выбранных видео" && videoList[i].Contains("_mute")) {
		filterScript += $"[{i}:a]stereotools=mutel=1:muter=1[amute{i}];";
	} else {
		filterScript += $"[{i}:a]acopy[amute{i}];";
	}
}
finalAudioSource = "amute";


// Наложение фоновой музыки на выбранных видео
for (i = 0; i < videoList.Count; i++) {
	if (bgMusic == "Выбранных видео" && videoList[i].Contains("_mute") && backgroundMusicList.Count > 0) {
		backgroundMusicList.Shuffle();
		sourceScript += $"-i \"{backgroundMusicList[0]}\" ";
		src++;
		
		// Добавление случайного фоновой музыки для видео
		filterScript += $"[{src}:a]volume={bgMusicVolume}[music{src}];";
		filterScript += $"[{finalAudioSource}{i}][music{src}]amix=inputs=2:duration=first[amusic{i}];";
	} else {
		// Если не выбрана опция с фоновой музыкой, тогда просто копируем аудио поток
		filterScript += $"[{finalAudioSource}{i}]acopy[amusic{i}];";
	}
}
finalAudioSource = "amusic";


// Объединяем видео в одно 
for (i = 0; i < videoList.Count; i++) {
	filterScript += $"[{finalVideoSource}{i}][{finalAudioSource}{i}]";
}
filterScript += $"concat=n={videoList.Count}:v=1:a=1[concatv][concata];";
// Если будет создание только видео, то финальный рендер попадает только эти ресурсы
finalVideoSource = "concatv";
finalAudioSource = "concata";


// Убираем звуки со всех видео
if (audioMute == "Всех видео") {
	filterScript += $"[{finalAudioSource}]stereotools=mutel=1:muter=1[concatamute];";
	finalAudioSource = "concatamute";
}

// TODO: Add text to video
bool useVideoText = Convert.ToBoolean(project.Variables["cfg_video_use_text"].Value);

if (useVideoText) {
	string videoTextTop = project.Variables["cfg_video_text_top"].Value;
	string videoTextTopCoordY = project.Variables["cfg_video_text_top_coord_y"].Value;
	
	string videoTextBottom = project.Variables["cfg_video_text_bottom"].Value;
	string videoTextBottomCoordY = project.Variables["cfg_video_text_bottom_coord_y"].Value;
	
	string videoTextFontName = project.Variables["cfg_video_text_font"].Value;
	string videoTextColor = project.Variables["cfg_video_text_color"].Value.ToLower();
	int videoTextWidth = Convert.ToInt32(project.Variables["cfg_video_text_widh"].Value);
	int videoTextHeight = Convert.ToInt32(project.Variables["cfg_video_text_height"].Value);
	
	// https://stackoverflow.com/questions/52691650/how-to-obtain-a-font-file-path-in-c
	
	// Находим путь к шрифту
	string fontFile = Path.GetFileName(ffmpeg.GetFontPath(videoTextFontName));
	project.SendInfoToLog("Шрифт для видео: " + fontFile);
	
	// Верхний текст
	int videoFontSize = 0;
	if (!string.IsNullOrWhiteSpace(videoTextTop)) {
		// Определяем размер шрифта
		videoFontSize = ffmpeg.GetFontSize(videoTextTop, videoTextFontName, "Regular", videoTextWidth, videoTextHeight, videoWidth, videoHeight);
		project.SendInfoToLog("Размер верхнего текста: " + videoFontSize.ToString());
		
		filterScript += $"[{finalVideoSource}]drawtext=text='{videoTextTop}':fontfile=\"/Windows/Fonts/{fontFile}\":x=({videoTextWidth}-tw)/2:y={videoTextTopCoordY}:fontsize={videoFontSize}:fontcolor={videoTextColor}[toptextv];";
		finalVideoSource = "toptextv";
		
		if (productRepeat) {
			filterScript += $"[{finalVideoSource}]drawtext=text='{videoTextTop}':fontfile=\"/Windows/Fonts/{fontFile}\":x=(W-{videoTextWidth})+({videoTextWidth}-tw)/2:y={videoTextTopCoordY}:fontsize={videoFontSize}:fontcolor={videoTextColor}[rtoptexttextv];";
			finalVideoSource = "rtoptexttextv";
		}
	}
	
	// Нижний текст
	if (!string.IsNullOrWhiteSpace(videoTextBottom)) {
		videoFontSize = ffmpeg.GetFontSize(videoTextBottom, videoTextFontName, "Regular", videoTextWidth, videoTextHeight, videoWidth, videoHeight);
		project.SendInfoToLog("Размер нижнего текста: " + videoFontSize.ToString());
		
		filterScript += $"[{finalVideoSource}]drawtext=text='{videoTextBottom}':fontfile=\"/Windows/Fonts/{fontFile}\":x=({videoTextWidth}-tw)/2:y={videoTextBottomCoordY}:fontsize={videoFontSize}:fontcolor={videoTextColor}[bottomtextv];";
		finalVideoSource = "bottomtextv";
		
		if (productRepeat) {
			filterScript += $"[{finalVideoSource}]drawtext=text='{videoTextBottom}':fontfile=\"/Windows/Fonts/{fontFile}\":x=(W-{videoTextWidth})+({videoTextWidth}-tw)/2:y={videoTextBottomCoordY}:fontsize={videoFontSize}:fontcolor={videoTextColor}[rbottomtexttextv];";
			finalVideoSource = "rbottomtexttextv";
		}
	}
}
 

// Наложение фоновой музыки по всей длине уже готового видео
if (bgMusic == "Всех видео" && backgroundMusicList.Count > 0) {
	backgroundMusicList.Shuffle();
	sourceScript += $"-i \"{backgroundMusicList[0]}\" ";
	src++;
	
	filterScript += $"[{src}:a]volume={bgMusicVolume}[bgmusic];";
	filterScript += $"[{finalAudioSource}][bgmusic]amix=inputs=2:duration=first[bgaudio];";
	
	finalAudioSource = "bgaudio";
}


// Накладываем лого или вотермарк на видео
bool useWatermark = Convert.ToBoolean(project.Variables["cfg_watermark_is_use"].Value);
string watermarkCoordWidth = project.Variables["cfg_watermark_coord_width"].Value;
string watermarkCoordHeight = project.Variables["cfg_watermark_coord_height"].Value;
string watermarkHeight = project.Variables["cfg_watermark_height"].Value;
string watermark = project.Variables["cfg_watermark_path"].Value;

if (useWatermark && !String.IsNullOrWhiteSpace(watermark)) {
	sourceScript += $"-i \"{watermark}\" ";
	src++;
	
	filterScript += $"[{src}:v]scale=-1:{watermarkHeight}[watermark];";
	filterScript += $"[{finalVideoSource}][watermark]overlay={watermarkCoordWidth}:{watermarkCoordHeight}[watermarkv];";
	
	finalVideoSource = "watermarkv";
}


// Intro
string intro = project.Variables["cfg_intro_path"].Value;
if (!String.IsNullOrWhiteSpace(intro)) {
	// Добавляем ресурс intro
	sourceScript += $"-i \"{intro}\" ";
	src++;
	
	filterScript += $"[{src}:v]scale=-1:{videoHeight},setsar=1,crop={videoWidth}:{videoHeight}[intro];";
	filterScript += $"[intro][{src}:a][{finalVideoSource}][{finalAudioSource}]concat=n=2:v=1:a=1[introv][introa];";
	
	finalVideoSource = "introv";
	finalAudioSource = "introa";
}


// Outro
string outro = project.Variables["cfg_outro_path"].Value;
if (!String.IsNullOrWhiteSpace(outro)) {
	// Добавляем ресурс outro
	sourceScript += $"-i \"{outro}\" ";
	src++;
	
	filterScript += $"[{src}:v]scale=-1:{videoHeight},setsar=1,crop={videoWidth}:{videoHeight}[outro];";
	filterScript += $"[{finalVideoSource}][{finalAudioSource}][outro][{src}:a]concat=n=2:v=1:a=1[outrov][outroa];";
	
	finalVideoSource = "outrov";
	finalAudioSource = "outroa";
}


// Возможность кодирования с аппаратным ускорением
// gpu encoding https://www.rickmakes.com/ffmpeg-hardware-encoding-on-a-windows-10/
bool useGPU = Convert.ToBoolean(project.Variables["cfg_use_gpu"].Value);
string videoCodec = "";
if (useGPU) {
	videoCodec = "h264_qsv";
} else {
	videoCodec = "libx264";
}


// Output video path
string workFolder = project.Variables["work_folder"].Value;
string outputVideo = System.IO.Path.Combine(workFolder, "video.mp4");

// Final script
codecsScript += $"\" -map [{finalVideoSource}] -map [{finalAudioSource}] -c:v {videoCodec} -c:a aac -r 30 \"{outputVideo}\"";

// Создаем видео
string resultScript = sourceScript + filterScript.TrimEnd(';') + codecsScript;
ffmpeg.StartFFMpeg(resultScript);

project.Variables["mp4_video"].Value = outputVideo;



/*
  Сделать вариант работы для обычных компиляций скачанных с других ютуб каналов, а именно:
  Выбирается канал вручную с подходящими компиляциями, чтобы знать где находится вотермарк на видео,
  та и вообще качество видео. Скачивается с канала видео, обрезается по времени (интро и аутро) при необходимости
  указывается в настройках, как правило на каналах не меняются эти настройки.
  Сохраняется тайтл видео в названии самого файла и обложка, чтобы подготовить на основе этого похожий заголовок и обложку.
  
  Дальше идет обработка видео (каждая настройка опциональная, т.е. можно выключить или включить):
  наложение видеошума (ffmpeg life), горизонтальное отзеркаливание, уменьшение размера (указать размер рамки в пикселях)
  и вставка на фон размытое видео либо добавление статического фона либо поверх видеорамку, размытие или скрытие вотермарка,
  ускорение или замедление видео вместе со звуком.
  
  Подготовка обложки, делать скриншоты через определенное время и накладывать на них рамку или смайл или текст

  Подготовка к публикации: подготоавливаются теги через rapidtags чтобы не были похожи сильно теги с скаченным видео, подрезка
  тегов по длине, 

*/