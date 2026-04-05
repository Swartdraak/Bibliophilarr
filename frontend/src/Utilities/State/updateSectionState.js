function updateSectionState(state, section, newState) {
  const [, subSection] = section.split('.');

  if (subSection) {
    return { ...state, [subSection]: newState };
  }

  // NOTE: Legacy section update — consider using subSection pattern for new code
  if (state.hasOwnProperty(section)) {
    return { ...state, [section]: newState };
  }

  return { ...state, ...newState };
}

export default updateSectionState;
