import { render, screen } from '@testing-library/react';
import React, { Component } from 'react';
import ReactSlider from 'react-slider';
// Require the ResizeObserver polyfill module so jest's V8 coverage provider
// instruments it. react-slider 2.0.0's componentDidMount calls new
// ResizeObserver(...) which jsdom does not implement; the polyfill provides the
// API contract. Loaded here (not in jest.setup.js) so the module is instrumented
// by the V8 coverage provider (setup files are not instrumented by V8).
require('../../../../build/resizeObserverPolyfill.js');

// This test drives the ACTUAL installed react-slider 2.0.6 from node_modules —
// the real shipped runtime module that PR #302 bumped (1.3.3 -> 2.0.6) — not a
// test-only substitute. It uses the EXACT prop shape the consumer
// frontend/src/Settings/Quality/Definition/QualityDefinition.js passes to
// <ReactSlider> (a two-value range slider), so it is the authoritative
// runtime-regression check for this RUNTIME major bump.
//
// react-slider 2.0.x wires keyboard interaction via the thumb's onFocus handler
// (createOnKeyDown) + a bubbled native keydown. We dispatch a real KeyboardEvent
// (bubbles=true) so the shipped keydown handler (moveUpByStep) runs.
// The ResizeObserver polyfill lives in the shared jest.setup.js (required because
// react-slider 2.0.0's componentDidMount calls new ResizeObserver(...), which
// jsdom does not implement).

// ---- Mirrors the real consumer: QualityDefinition.js
const MIN = 0;
const MAX = 1500;
const MIN_DISTANCE = 1;

const slider = {
  min: MIN,
  max: Math.round(Math.pow(MAX, 1 / 1.1) * 100) / 100, // roundNumber(Math.pow(MAX, 1/1.1))
  step: 0.1
};

function makeArrowRightKey() {
  const ev = new KeyboardEvent('keydown', {
    key: 'ArrowRight',
    code: 'ArrowRight',
    bubbles: true,
    cancelable: true
  });
  Object.defineProperty(ev, 'keyCode', { value: 39 });
  Object.defineProperty(ev, 'which', { value: 39 });
  return ev;
}

// Minimal host that drives the shipped react-slider with the EXACT prop shape
// QualityDefinition.js uses (range/two-value).
class RangeSliderHost extends Component {
  constructor(props) {
    super(props);
    this.state = { value: [20, 300] };
  }

  onSliderChange = (next) => {
    this.setState({ value: next });
  };

  onAfterSliderChange = () => {
    // Mirror QualityDefinition.onAfterSliderChange: re-derive from props.
    this.setState({ value: [20, 300] });
  };

  render() {
    const [sliderMinSize, sliderMaxSize] = this.state.value;

    return (
      <ReactSlider
        min={slider.min}
        max={slider.max}
        step={slider.step}
        minDistance={MIN_DISTANCE * 5}
        value={[sliderMinSize, sliderMaxSize]}
        withTracks={true}
        snapDragDisabled={true}
        onChange={this.onSliderChange}
        onAfterChange={this.onAfterSliderChange}
      />
    );
  }
}

describe('react-slider 2.0.6 runtime (range usage as shipped by QualityDefinition)', () => {
  test('renders two thumbs and both tracks from a value array (range API intact)', () => {
    const { container } = render(<RangeSliderHost />);

    // Two-value (range) API: 2.0.0 did NOT remove the array `value` form.
    const thumbs = screen.getAllByRole('slider');
    expect(thumbs).toHaveLength(2);
    expect(thumbs[0].getAttribute('aria-valuenow')).toBe('20');
    expect(thumbs[1].getAttribute('aria-valuenow')).toBe('300');

    // withTracks={true} renders one track per segment (>= value.length).
    const trackEls = container.querySelectorAll('[class*="track"]');
    expect(trackEls.length).toBeGreaterThanOrEqual(2);
  });

  test('propagates a two-value onChange array from the live slider (onChange contract intact)', () => {
    render(<RangeSliderHost />);

    const thumbs = screen.getAllByRole('slider');

    // Focus the second thumb (react-slider attaches keyboard via onFocus),
    // then dispatch a real ArrowRight keydown so the shipped moveUpByStep runs.
    thumbs[1].focus();
    const moved = thumbs[1].dispatchEvent(makeArrowRightKey());

    // A handled keydown calls e.preventDefault() -> dispatchEvent returns false.
    expect(moved).toBe(false);

    const thumbsAfter = screen.getAllByRole('slider');
    const secondValue = Number(thumbsAfter[1].getAttribute('aria-valuenow'));

    // step=0.1; one ArrowRight press increases the second thumb value.
    expect(secondValue).toBeGreaterThan(300);
    // The first thumb (min of the range) is unchanged by a second-thumb move.
    expect(Number(thumbsAfter[0].getAttribute('aria-valuenow'))).toBe(20);
  });

  test('accepts the withTracks / snapDragDisabled / minDistance props without erroring', () => {
    expect(() => {
      render(
        <ReactSlider
          min={slider.min}
          max={slider.max}
          step={slider.step}
          minDistance={MIN_DISTANCE * 5}
          value={[20, 300]}
          withTracks={true}
          snapDragDisabled={true}
          onChange={() => {}}
          onAfterChange={() => {}}
        />
      );
    }).not.toThrow();
  });
});
