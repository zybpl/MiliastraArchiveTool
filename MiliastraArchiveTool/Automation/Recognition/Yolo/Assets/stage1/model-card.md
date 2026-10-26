# Miliastra Stage1 8D Info Panel YOLO Model Card

## Source

- Training result: `data\千星奇域训练截图\_runs\stage1_8d_info_panel`
- Training dataset: `data\千星奇域训练截图\_yolo_stage1_8d_info_panel`
- Smoke output: `data\千星奇域训练截图\_offline_smoke_stage1_8d_info_panel`
- Classes: 76
- Images: 1468
- Boxes: 10159
- Added info_panel_row boxes: 72

## Training Metrics

- precision: 0.96922
- recall: 0.99007
- mAP50: 0.98715
- mAP50-95: 0.87879

## Integration Notes

- `info_panel_row` is an uploaded-archive right-panel row anchor. Use OCR/context to choose Preview Info or Image Assets.
- Do not reintroduce old `uploaded_preview_info_button` click logic; use `text_edit_icon` inside the OCR-selected row for preview editing.
- Never click publish/post targets automatically.
