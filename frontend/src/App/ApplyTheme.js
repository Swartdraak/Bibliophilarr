import PropTypes from 'prop-types';
import React, { Fragment, useEffect } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import themes from 'Styles/Themes';

function createMapStateToProps() {
  return createSelector(
    (state) => state.settings.ui.item.theme || window.Bibliophilarr.theme,
    (
      theme
    ) => {
      return {
        theme
      };
    }
  );
}

export function ApplyTheme({ theme, children }) {
  useEffect(() => {
    const fixedTheme = theme === 'light' || theme === 'dark';
    const preference = fixedTheme ? null : window.matchMedia?.('(prefers-color-scheme: dark)');
    const update = () => {
      const systemPalette = preference?.matches ? 'dark' : 'light';
      const palette = fixedTheme ? theme : systemPalette;
      const style = document.documentElement.style;
      Object.entries(themes[palette]).forEach(([key, value]) => style.setProperty(`--${key}`, value));
      style.colorScheme = palette;
    };

    update();
    if (preference?.addEventListener) {
      preference.addEventListener('change', update);
      return () => preference.removeEventListener('change', update);
    }

    preference?.addListener?.(update);
    return () => preference?.removeListener?.(update);
  }, [theme]);

  return <Fragment>{children}</Fragment>;
}

ApplyTheme.propTypes = {
  theme: PropTypes.string.isRequired,
  children: PropTypes.object.isRequired
};

export default connect(createMapStateToProps)(ApplyTheme);
