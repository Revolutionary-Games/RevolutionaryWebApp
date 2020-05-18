# frozen_string_literal: true

# Updates the git files of an lfs project
class RefreshGitFiles < Hyperstack::ServerOp
  param :acting_user
  param :project_id
  add_error(:project_id, :does_not_exist, 'project does not exist') {
    !(@project = LfsProject.find_by_id(params.project_id))
  }
  validate { params.acting_user.developer? }
  step {
    # Clear the commit to make refresh actually always happe
    @project.file_tree_commit = nil
  }
  step {
    @project.save!
  }
  step {
    RefreshLfsProjectFiles.perform_later @project.id
  }
end
