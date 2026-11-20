import PropTypes from 'prop-types';
import React, { useRef } from 'react';
import Icon from 'Components/Icon';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';
import MenuItemSeparator from 'Components/Menu/MenuItemSeparator';
import { align, icons, kinds } from 'Helpers/Props';
import styles from './PageHeaderActionsMenu.css';

function PageHeaderActionsMenu(props) {
  const logoutForm = useRef(null);
  const {
    formsAuth,
    onKeyboardShortcutsPress,
    onRestartPress,
    onShutdownPress
  } = props;

  return (
    <div>
      {
        formsAuth &&
          <form
            ref={logoutForm}
            method="POST"
            action={`${window.Bibliophilarr.urlBase}/logout`}
            hidden={true}
          >
            <input
              type="hidden"
              name="__RequestVerificationToken"
              value={window.Bibliophilarr.requestVerificationToken}
            />
          </form>
      }
      <Menu alignMenu={align.RIGHT}>
        <MenuButton className={styles.menuButton} aria-label="Menu Button">
          <Icon
            name={icons.INTERACTIVE}
          />
        </MenuButton>

        <MenuContent>
          <MenuItem onPress={onKeyboardShortcutsPress}>
            <Icon
              className={styles.itemIcon}
              name={icons.KEYBOARD}
            />
            Keyboard Shortcuts
          </MenuItem>

          <MenuItemSeparator />

          <MenuItem onPress={onRestartPress}>
            <Icon
              className={styles.itemIcon}
              name={icons.RESTART}
            />
            Restart
          </MenuItem>

          <MenuItem onPress={onShutdownPress}>
            <Icon
              className={styles.itemIcon}
              name={icons.SHUTDOWN}
              kind={kinds.DANGER}
            />
            Shutdown
          </MenuItem>

          {
            formsAuth &&
              <div className={styles.separator} />
          }

          {
            formsAuth &&
              <MenuItem
                onPress={() => logoutForm.current.requestSubmit()}
              >
                <Icon
                  className={styles.itemIcon}
                  name={icons.LOGOUT}
                />
                Logout
              </MenuItem>
          }
        </MenuContent>
      </Menu>
    </div>
  );
}

PageHeaderActionsMenu.propTypes = {
  formsAuth: PropTypes.bool.isRequired,
  onKeyboardShortcutsPress: PropTypes.func.isRequired,
  onRestartPress: PropTypes.func.isRequired,
  onShutdownPress: PropTypes.func.isRequired
};

export default PageHeaderActionsMenu;
