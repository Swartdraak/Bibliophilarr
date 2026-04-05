import _ from 'lodash';

function getSectionState(state, section, isFullStateTree = false) {
  if (isFullStateTree) {
    return _.get(state, section);
  }

  const [, subSection] = section.split('.');

  if (subSection) {
    return { ...state[subSection] };
  }

  // NOTE: Legacy section lookup — consider using subSection pattern for new code
  if (state.hasOwnProperty(section)) {
    return { ...state[section] };
  }

  return { ...state };
}

export default getSectionState;
