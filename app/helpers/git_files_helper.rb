# frozen_string_literal: true

require 'fileutils'
require 'uri'

LOCAL_GIT_TARGET_FOLDER ||= 'tmp/git_repos'

# Helper for cloning and inspecting the files of a git repo
module GitFilesHelper
  # The main method
  def self.update_files(lfs_project)
    if lfs_project.clone_url.blank?
      Rails.logger.warning 'Lfs project has blank clone url, skipping it'
      return
    end

    git_checkout lfs_project

    # Skip update if still the same commit
    commit = current_commit lfs_project
    if commit == lfs_project.file_tree_commit
      Rails.logger.info "Lfs project (#{lfs_project.name}) has up" \
                        ' to date files for current commit'
      return
    end

    add_and_update_file_objects lfs_project
    delete_non_existant_objects lfs_project

    lfs_project.file_tree_commit = commit
    lfs_project.file_tree_updated = Time.now
    lfs_project.save!
  end

  def self.delete_all_file_objects(lfs_project)
    # Clear the commit to make the rebuild work
    lfs_project.file_tree_commit = nil
    lfs_project.save!

    # TODO: delete all the ProjectGitFile objects
  end

  def self.add_and_update_file_objects(lfs_project)
    loop_local_files(lfs_project) { |file, inside_path|
      puts "#{inside_path} local path: #{file}"
    }
  end

  def self.delete_non_existant_objects(lfs_project); end

  def self.loop_local_files(lfs_project)
    search_start = folder(lfs_project)
    prefix = File.join search_start, ''

    Dir.glob(File.join(search_start, '**/*')) { |f|
      yield [f, f.sub(prefix, '')]
    }
  end

  def self.folder(lfs_project)
    File.join LOCAL_GIT_TARGET_FOLDER, File.basename(URI.parse(lfs_project.clone_url).path)
  rescue URI::InvalidURIError
    File.join LOCAL_GIT_TARGET_FOLDER, File.basename(lfs_project.clone_url)
  end

  def self.git_checkout(lfs_project)
    FileUtils.mkdir_p LOCAL_GIT_TARGET_FOLDER
    git_clone lfs_project unless File.exist? folder(lfs_project)

    Dir.chdir(folder(lfs_project)) {
      system env, 'git', 'checkout', 'master'
      system env, 'git', 'pull'
    }
  end

  def self.git_clone(lfs_project)
    system env, 'git', 'clone', lfs_project.clone_url, folder(lfs_project)

    if $CHILD_STATUS.exitstatus.nil? || !$CHILD_STATUS.exitstatus.zero?
      Rails.logger.error 'Git clone failed'
      raise 'Cloning git repo failed'
    end
  end

  def self.env
    { 'GIT_LFS_SKIP_SMUDGE' => '1' }
  end

  # Returns the current git commit
  def self.current_commit(lfs_project)
    Dir.chdir(folder(lfs_project)) {
      `git rev-parse HEAD`.strip
    }
  end
end
