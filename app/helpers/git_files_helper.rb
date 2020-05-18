# frozen_string_literal: true

require 'fileutils'
require 'uri'

LOCAL_GIT_TARGET_FOLDER ||= 'tmp/git_repos'

# Helper for cloning and inspecting the files of a git repo
module GitFilesHelper
  # The main method
  def self.update_files(lfs_project)
    if lfs_project.clone_url.blank?
      Rails.logger.warn 'Lfs project has blank clone url, skipping it'
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

    ProjectGitFile.where(lfs_project_id: lfs_project.id).destroy_all
  end

  def self.add_and_update_file_objects(lfs_project)
    folders = {}

    loop_local_files(lfs_project) { |file, inside_path|
      # Skip folders as we separately detect those from the existing objects
      next if File.directory? file

      dir = process_folder_path(File.dirname(inside_path))

      if folders.include? dir
        folders[dir] += 1
      else
        folders[dir] = 1
      end

      filename = File.basename inside_path

      oid, size = detect_lfs_file file

      size = File.size file if oid.nil?

      existing = ProjectGitFile.find_by lfs_project: lfs_project, name: filename,
                                        path: dir

      if !existing
        ProjectGitFile.create! lfs_project: lfs_project, name: filename, path: dir,
                               lfs_oid: oid, size: size, ftype: 'file'
      else
        existing.lfs_oid = oid
        existing.size = size
        existing.ftype = 'file'
        existing.save! if existing.changed?
      end
    }

    # Create folders
    folders.each { |folder, item_count|
      dir = process_folder_path(File.dirname(folder))
      filename = File.basename folder

      existing = ProjectGitFile.find_by lfs_project: lfs_project, name: filename,
                                        path: dir

      if !existing
        ProjectGitFile.create! lfs_project: lfs_project, name: filename, path: dir,
                               lfs_oid: nil, size: item_count, ftype: 'folder'
      else
        existing.lfs_oid = nil
        existing.size = item_count
        existing.ftype = 'folder'
        existing.save! if existing.changed?
      end
    }
  end

  def self.delete_non_existant_objects(lfs_project)
    local_base = folder lfs_project

    ProjectGitFile.where(lfs_project_id: lfs_project.id, ftype: 'file').find_each { |file|
      local_file = File.join(local_base, file.path, file.name)

      file.destroy unless File.exist? local_file
    }

    # TODO: delete empty folders
  end

  def self.process_folder_path(folder)
    if folder.blank? || folder == '.'
      '/'
    elsif folder[0] != '/'
      '/' + folder
    else
      folder
    end
  end

  def self.detect_lfs_file(file)
    File.open(file) { |f|
      data = f.read(4048)

      size = nil
      oid = nil

      if %r{.*version https://git-lfs\.github\.com.*}i.match?(data)
        # Lfs file
        data.each_line { |line|
          if (match = line.match(/oid sha256:(\w+)/i))
            oid = match.captures[0]
            next
          end
          if (match = line.match(/size (\d+)/i))
            size = match.captures[0].to_i
            next
          end
        }
      end

      return [oid, size]
    }

    [nil, nil]
  end

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

    if $CHILD_STATUS.nil? || !$CHILD_STATUS.exitstatus.zero?
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
