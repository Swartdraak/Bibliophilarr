import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { Redirect, Route, Switch } from 'react-router-dom';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons } from 'Helpers/Props';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import WikiPage from './WikiPage';
import wikiPages from './wikiPages';
import styles from './Wiki.css';

function WikiHome() {
  return (
    <div>
      <div className={styles.section}>
        <h2 className={styles.sectionTitle}>
          {translate('Wiki')}
        </h2>
        <p className={styles.paragraph}>
          Welcome to the Bibliophilarr built-in wiki. Browse the topics below or use the sidebar to navigate directly to a topic.
        </p>
      </div>

      <div className={styles.homeGrid}>
        {
          wikiPages.map((page) => {
            const iconName = icons[page.icon] || icons.INFO;

            return (
              <Link
                key={page.id}
                className={styles.homeCard}
                to={getPathWithUrlBase(`/system/wiki/${page.id}`)}
              >
                <div className={styles.homeCardTitle}>
                  <Icon
                    name={iconName}
                    size={16}
                  />
                  {page.title}
                </div>
                <p className={styles.homeCardDescription}>
                  {page.description}
                </p>
              </Link>
            );
          })
        }
      </div>
    </div>
  );
}

function WikiPageRoute({ match }) {
  const { pageId } = match.params;
  const page = wikiPages.find((p) => p.id === pageId);

  if (!page) {
    return <Redirect to={getPathWithUrlBase('/system/wiki')} />;
  }

  return <WikiPage page={page} />;
}

WikiPageRoute.propTypes = {
  match: PropTypes.object.isRequired
};

class Wiki extends Component {

  //
  // Render

  render() {
    return (
      <PageContent title={translate('Wiki')}>
        <PageContentBody>
          <div className={styles.wikiContainer}>
            <nav className={styles.sidebar}>
              <ul className={styles.sidebarList}>
                <li className={styles.sidebarItem}>
                  <Link
                    className={styles.sidebarLink}
                    to={getPathWithUrlBase('/system/wiki')}
                  >
                    Home
                  </Link>
                </li>
                {
                  wikiPages.map((page) => (
                    <li key={page.id} className={styles.sidebarItem}>
                      <Link
                        className={styles.sidebarLink}
                        to={getPathWithUrlBase(`/system/wiki/${page.id}`)}
                      >
                        {page.title}
                      </Link>
                    </li>
                  ))
                }
              </ul>
            </nav>

            <div className={styles.content}>
              <Switch>
                <Route
                  exact={true}
                  path="/system/wiki"
                  component={WikiHome}
                />
                <Route
                  path="/system/wiki/:pageId"
                  component={WikiPageRoute}
                />
              </Switch>
            </div>
          </div>
        </PageContentBody>
      </PageContent>
    );
  }
}

export default Wiki;
