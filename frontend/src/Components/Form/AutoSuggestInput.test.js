import AutoSuggestInput from './AutoSuggestInput';

test.each([
  ['bottom-start', window.innerHeight - 160],
  ['top-end', 120]
])('limits a %s suggestion list to the available space on its opening side', (placement, maxHeight) => {
  const input = new AutoSuggestInput({});
  const data = {
    placement,
    offsets: { reference: { top: 120, bottom: 160, width: 260 } },
    styles: {}
  };

  expect(input.onComputeMaxHeight(data).styles).toEqual({ maxHeight, width: 260 });
});
