package main

import (
	"fmt"
	"github.com/tidwall/gjson"
	ffmpeg "github.com/u2takey/ffmpeg-go"
	"io/ioutil"
	"log"
	"os"
)

// Настройки
type config struct {
	width              int
	height             int
	fps                int
	transitionDuration float64
	zoom               float64
	zoomSpeed          int
	zoomScale          float64
	slideDuration      float64
	padding            int
	bgBlur             int
	voiceFile          string
	imagesPath         string
	finalFileName      string
}

var conf = config{
	width:              1920,
	height:             1080,
	fps:                30,
	transitionDuration: 0.5,
	zoom:               1.5,
	zoomSpeed:          3,
	zoomScale:          0.0002,
	padding:            200,
	bgBlur:             50,
	voiceFile:          "voice.mp3",
	imagesPath:         "images",
	finalFileName:      "result.mp4",
}

func addZoompan(inputs []*ffmpeg.Stream) []*ffmpeg.Stream {
	var resultInputs []*ffmpeg.Stream
	for _, input := range inputs {
		input = input.Filter("scale", nil, ffmpeg.KwArgs{
			"w": "-1",
			"h": conf.height - conf.padding,
		}).Filter("scale", nil, ffmpeg.KwArgs{
			"w": "-1",
			"h": "ih*3",
		}).Filter("pad", nil, ffmpeg.KwArgs{
			"w": conf.width * 3,
			"h": conf.height * 3,
			"x": "(ow-iw)/2",
			"y": "(oh-ih)/2",
		}).ZoomPan(ffmpeg.KwArgs{
			"z": fmt.Sprintf("min(zoom+%f*%d,%f)", conf.zoomScale, conf.zoomSpeed, conf.zoom),
			//"x": "trunc(iw/2-(iw/zoom/2))", // zoom to top center
			//"y":   "y",
			"x":   "iw/2-(iw/zoom/2)", // zoom to center
			"y":   "ih/2-(ih/zoom/2)", // zoom to center
			"d":   conf.slideDuration * float64(conf.fps),
			"fps": conf.fps,
			"s":   fmt.Sprintf("%dx%d", conf.width, conf.height),
		}).Trim(ffmpeg.KwArgs{"duration": conf.slideDuration})
		resultInputs = append(resultInputs, input)
	}

	return resultInputs
}

func addZoompanOverlay(inputs []*ffmpeg.Stream) []*ffmpeg.Stream {
	var resultInputs []*ffmpeg.Stream
	for _, inputBase := range inputs {
		streams := inputBase.Split()
		input1, input2 := streams.Get("0"), streams.Get("1")

		slide := input1.Filter("scale", nil, ffmpeg.KwArgs{
			"w": "-1",
			"h": conf.height - conf.padding,
		})

		bg := input2.Filter("scale", nil, ffmpeg.KwArgs{
			"w": conf.width,
			"h": conf.height,
		}).Filter("boxblur", ffmpeg.Args{fmt.Sprintf("%d", conf.bgBlur)})

		slide = ffmpeg.Filter([]*ffmpeg.Stream{bg, slide}, "overlay", nil, ffmpeg.KwArgs{
			"x":        "((W-w)/2)",
			"y":        "((H-h)/2)",
			"shortest": 1,
		}).Filter("scale", nil, ffmpeg.KwArgs{
			"w": "-1",
			"h": "ih*3",
		})

		slide = slide.ZoomPan(ffmpeg.KwArgs{
			"z": fmt.Sprintf("min(zoom+%f*%d,%f)", conf.zoomScale, conf.zoomSpeed, conf.zoom),
			//"x": "trunc(iw/2-(iw/zoom/2))", // zoom to top center
			//"y":   "y",
			"x":   "iw/2-(iw/zoom/2)", // zoom to center
			"y":   "ih/2-(ih/zoom/2)", // zoom to center
			"d":   conf.slideDuration * float64(conf.fps),
			"fps": conf.fps,
			"s":   fmt.Sprintf("%dx%d", conf.width, conf.height),
		}).Trim(ffmpeg.KwArgs{"duration": conf.slideDuration})

		resultInputs = append(resultInputs, slide)
	}

	return resultInputs
}

func addTransitions(inputs []*ffmpeg.Stream, transition string, slideDuration float64, transitionDuration float64) *ffmpeg.Stream {
	// https://stackoverflow.com/questions/63553906/merging-multiple-video-files-with-ffmpeg-and-xfade-filter
	// https://trac.ffmpeg.org/wiki/Xfade

	var result *ffmpeg.Stream
	prevOffset := 0.0

	for i, input := range inputs {
		if i == 0 {
			continue
		}

		offset := slideDuration + prevOffset - transitionDuration/2

		opt := ffmpeg.KwArgs{
			"transition": transition,
			"duration":   transitionDuration,
			"offset":     offset,
		}

		if i == 1 {
			fadeInputs := []*ffmpeg.Stream{inputs[0], inputs[1]}
			result = ffmpeg.Filter(fadeInputs, "xfade", nil, opt)
		} else {
			fadeInputs := []*ffmpeg.Stream{result, input}
			result = ffmpeg.Filter(fadeInputs, "xfade", nil, opt)
		}

		prevOffset = offset
	}

	return result
}

func getVoiceDuration(path string) float64 {
	probeVoice, err := ffmpeg.Probe(path)
	if err != nil {
		log.Fatal(err)
	}
	return gjson.Get(probeVoice, "format.duration").Float()
}

func getFiles(path string) []string {
	files, err := ioutil.ReadDir(path)
	if err != nil {
		log.Fatal(err)
	}
	var result []string
	for _, file := range files {
		if !file.IsDir() {
			result = append(result, path+"/"+file.Name())
		}
	}

	return result
}

func main() {
	voiceDuration := getVoiceDuration(conf.voiceFile)
	fmt.Println(voiceDuration)

	images := getFiles(conf.imagesPath)
	var slideDuration = (voiceDuration - (float64(len(images))-1)*conf.transitionDuration*2) / float64(len(images))
	conf.slideDuration = slideDuration

	var inputs []*ffmpeg.Stream
	for _, image := range images {
		input := ffmpeg.Input(image, ffmpeg.KwArgs{"framerate": conf.fps, "t": slideDuration, "loop": 1})
		inputs = append(inputs, input)
	}

	var streams []*ffmpeg.Stream

	// Intro stream
	var introVideo *ffmpeg.Stream
	var introAudio *ffmpeg.Stream
	introFile := "intro.mp4"
	if _, err := os.Stat(introFile); err == nil {
		introVideo = ffmpeg.Input(introFile).Filter("scale", nil, ffmpeg.KwArgs{"width": conf.width, "height": -1})
		introAudio = ffmpeg.Input(introFile).Audio()
		streams = append(streams, introVideo, introAudio)
	}

	// Voice stream
	if _, err := os.Stat(conf.voiceFile); err != nil {
		log.Fatal("Do not find voice file")
	}
	voice := ffmpeg.Input(conf.voiceFile)

	// Add transitions with Zoom Pan
	video := addTransitions(addZoompanOverlay(inputs), "fade", slideDuration, conf.transitionDuration)

	streams = append(streams, video, voice)
	video = ffmpeg.Concat(streams, ffmpeg.KwArgs{"v": 1, "a": 1, "unsafe": true})

	// Output options
	opt := ffmpeg.KwArgs{
		"vcodec": "h264_qsv",
		"preset": "fast",
		"r":      conf.fps,
		"acodec": "aac",
	}

	// Output video
	err := ffmpeg.Output([]*ffmpeg.Stream{video}, conf.finalFileName, opt).OverWriteOutput().ErrorToStdOut().Run()
	if err != nil {
		log.Fatal(err)
	}

}
