# RapidOCR Chinese/English Workflow OCR

This OCR asset bundle is used by both Stage1 local archive card selection and
workflow text recognition (buttons, preview fields, and image assets).

- Engine: RapidOcrNet 4.0.2
- Runtime: local ONNX inference
- Detection model: ch_PP-OCRv5_mobile_det.onnx
- Classification model: ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx
- Recognition model: ch_PP-OCRv5_rec_server.onnx (local ONNX inference; no remote server)
- Charset: ppocrv5_dict.txt
- Intended workflow languages: Chinese and English

## Recognition model provenance

Switched from the Latin recognition model and dictionary on 2026-09-06.
Keep the recognition model and dictionary paired; replacing only the dictionary
does not add Chinese recognition to a Latin model.

- Official model list: https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/default_models.yaml
- Model: https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_server.onnx
- Model SHA256: `e09385400eaaaef34ceff54aeb7c4f0f1fe014c27fa8b9905d4709b65746562a`
- Dictionary: https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_server/ppocrv5_dict.txt
- Dictionary SHA256: `d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b`

The former `latin_PP-OCRv5_rec_mobile_infer.onnx` and `ppocrv5_latin_dict.txt`
are retained for offline comparisons; the application does not load them.
Detection, orientation classification, ROI scaling, and confidence thresholds
remain unchanged. This change does not validate the multilingual table workflow.

## Offline verification

From the workspace root, run:

```powershell
dotnet run --project .\_scratch\OcrBilingualProbe\OcrBilingualProbe.csproj -- (Get-Location).Path
```

The probe checks recognition-output/dictionary dimensions and compares the old
and new models on Chinese, English, and mixed-text regions in existing game
screenshots. It also exercises `RapidOcrWorkflowOcrService` and writes
`_scratch/OcrBilingualProbe/report.json`. It does not interact with the game.

Result on 2026-09-06: 9/9 regions passed both card-recognizer and workflow-ROI
checks. Covered Chinese preview labels and submit, Chinese names with digits,
and English resource-title/Open Drafts/Import Save text. The model output has
18,385 classes, matching 18,383 dictionary entries plus CTC blank and space.
The Chinese mobile candidate passed 8/9: the workflow ROI read `Open Drafts`
as `0pen Drafts`. The server recognition model passed that case as well.
Its model file is 84,577,022 bytes. Observed warm card-recognition times for these
small cropped regions were 133–231 ms, with the first call including model
initialization taking 605 ms. These are local smoke results, not a full-screen
benchmark or a successful end-to-end upload test.

The upload workflow uses YOLO local_archive_card detections as native-pixel ROIs, runs OCR locally, and only accepts a card when the current run archive code is uniquely matched. It must not fall back to Last Opened, top-left, or latest-card heuristics.

On 2026-09-07, DBNet `UnClipRatio` was reduced from the library default 1.6 to
1.0. On run `20260907-012510684-2ee45f46e`, the original Early Access header
and placeholder crops were complete, but excessive text-line expansion degraded
server recognition. The tighter boxes recover both phrases without application
upscaling. Four first-frame text regions and two actual field-selection checks
pass in `_scratch/PreviewContextOcrProbe`, alongside the 9 bilingual and 6 archive
OCR checks. These are offline regressions, not a live upload completion.
