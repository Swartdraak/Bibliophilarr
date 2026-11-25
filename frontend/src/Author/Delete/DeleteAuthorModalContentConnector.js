import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { deleteAuthor } from 'Store/Actions/authorActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import DeleteAuthorModalContent from './DeleteAuthorModalContent';

function createMapStateToProps() {
  return createSelector(
    createAuthorSelector(),
    (author) => {
      return author;
    }
  );
}

const mapDispatchToProps = {
  deleteAuthor
};

class DeleteAuthorModalContentConnector extends Component {

  state = {
    isDeleting: false,
    deleteError: null
  };

  componentWillUnmount() {
    this.isUnmounted = true;
  }

  //
  // Listeners

  onDeletePress = (deleteFiles, addImportListExclusion) => {
    if (this.state.isDeleting) {
      return;
    }

    this.setState({ isDeleting: true, deleteError: null });
    const request = this.props.deleteAuthor({
      id: this.props.authorId,
      deleteFiles,
      addImportListExclusion
    });

    request.done(() => {
      if (!this.isUnmounted) {
        this.setState({ isDeleting: false });
        this.props.onModalClose(true);
      }
    });

    request.fail((deleteError) => {
      if (!this.isUnmounted) {
        this.setState({ isDeleting: false, deleteError });
      }
    });
  };

  //
  // Render

  render() {
    return (
      <DeleteAuthorModalContent
        {...this.props}
        {...this.state}
        onDeletePress={this.onDeletePress}
      />
    );
  }
}

DeleteAuthorModalContentConnector.propTypes = {
  authorId: PropTypes.number.isRequired,
  onModalClose: PropTypes.func.isRequired,
  deleteAuthor: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(DeleteAuthorModalContentConnector);
