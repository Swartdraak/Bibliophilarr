function getSupportedFormats(profile) {
  const qualities = (profile.items || [])
    .filter((item) => item.allowed)
    .flatMap((item) => (item.quality ? [item.quality] : (item.items || []).map(({ quality }) => quality)));

  // Match the quality IDs in NzbDrone.Core.Qualities.Quality.GetFormatType.
  return {
    ebook: qualities.some((quality) => quality?.id >= 0 && quality.id <= 4),
    audiobook: qualities.some((quality) => quality?.id >= 10 && quality.id <= 13)
  };
}

function getFormatSetting(setting, profiles, format, otherFormat) {
  const matching = profiles.filter(({ formats }) => formats[format]);
  const selected = matching.find(({ id }) => id === Number(setting?.value)) ||
    matching.find(({ formats }) => !formats[otherFormat]) ||
    matching[0];

  return {
    ...setting,
    value: selected?.id,
    allowedProfileIds: matching.map(({ id }) => id)
  };
}

function getAddQualityProfileSettings(settings, qualityProfiles) {
  const profiles = qualityProfiles.map((profile) => ({
    id: profile.id,
    formats: getSupportedFormats(profile)
  }));
  const ebookQualityProfileId = getFormatSetting(settings.ebookQualityProfileId, profiles, 'ebook', 'audiobook');
  const audiobookQualityProfileId = getFormatSetting(settings.audiobookQualityProfileId, profiles, 'audiobook', 'ebook');
  const baseProfile = profiles.find(({ id }) => id === Number(settings.qualityProfileId?.value));

  return {
    qualityProfileId: {
      ...settings.qualityProfileId,
      value: baseProfile?.id ?? ebookQualityProfileId.value ?? audiobookQualityProfileId.value ?? profiles[0]?.id ?? 0
    },
    ebookQualityProfileId,
    audiobookQualityProfileId
  };
}

export default getAddQualityProfileSettings;
