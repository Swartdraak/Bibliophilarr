import getAddQualityProfileSettings from './getAddQualityProfileSettings';

const ebook = { id: 1, name: 'eBook', items: [{ allowed: true, quality: { id: 3 } }] };
const audiobook = {
  id: 2,
  name: 'aBook',
  items: [
    { allowed: true, items: [{ quality: { id: 10 } }, { quality: { id: 12 } }] },
    { allowed: false, quality: { id: 3 } }
  ]
};
const mixed = { id: 3, name: 'Any', items: [...ebook.items, ...audiobook.items] };
const unavailable = {
  id: 4,
  name: 'No enabled qualities',
  items: [{ allowed: false, items: [{ quality: { id: 11 } }] }]
};
const profiles = [mixed, unavailable, audiobook, ebook];

test('prefers dedicated profiles by their enabled qualities, regardless of name or order', () => {
  expect(getAddQualityProfileSettings({}, profiles)).toEqual({
    qualityProfileId: { value: 1 },
    ebookQualityProfileId: { value: 1, allowedProfileIds: [3, 1] },
    audiobookQualityProfileId: { value: 2, allowedProfileIds: [3, 2] }
  });
});

test('preserves valid explicit choices, including mixed and numeric strings', () => {
  const settings = {
    qualityProfileId: { value: 2 },
    ebookQualityProfileId: { value: 3, warnings: [] },
    audiobookQualityProfileId: { value: '3' }
  };

  expect(getAddQualityProfileSettings(settings, profiles)).toEqual({
    qualityProfileId: { value: 2 },
    ebookQualityProfileId: { value: 3, warnings: [], allowedProfileIds: [3, 1] },
    audiobookQualityProfileId: { value: 3, allowedProfileIds: [3, 2] }
  });
});

test('replaces stale or incompatible saved choices without mutating them', () => {
  const settings = {
    qualityProfileId: { value: 99 },
    ebookQualityProfileId: { value: 2 },
    audiobookQualityProfileId: { value: 99 }
  };

  expect(getAddQualityProfileSettings(settings, profiles)).toMatchObject({
    qualityProfileId: { value: 1 },
    ebookQualityProfileId: { value: 1 },
    audiobookQualityProfileId: { value: 2 }
  });
  expect(settings.audiobookQualityProfileId.value).toBe(99);
});

test('uses a mixed profile when it is the only suitable choice', () => {
  expect(getAddQualityProfileSettings({}, [mixed])).toMatchObject({
    ebookQualityProfileId: { value: 3 },
    audiobookQualityProfileId: { value: 3 }
  });
});

test('leaves a format unset when no profile supports it', () => {
  expect(getAddQualityProfileSettings({}, [ebook, unavailable])).toMatchObject({
    qualityProfileId: { value: 1 },
    ebookQualityProfileId: { value: 1 },
    audiobookQualityProfileId: { value: undefined, allowedProfileIds: [] }
  });

  expect(getAddQualityProfileSettings({}, [])).toMatchObject({
    qualityProfileId: { value: 0 },
    ebookQualityProfileId: { value: undefined, allowedProfileIds: [] },
    audiobookQualityProfileId: { value: undefined, allowedProfileIds: [] }
  });
});
